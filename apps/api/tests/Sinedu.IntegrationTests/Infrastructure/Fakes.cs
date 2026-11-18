using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Storage;
using Sinedu.Tenant.Access;

namespace Sinedu.IntegrationTests.Infrastructure;

/// <summary>Vault em memória: mesmas operações (transit + KV), chaves AES por nome.</summary>
public sealed class InMemoryVault : IDataKeyProvider, IKeyManagement, ISecretCipher, ISecretStore
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = new();
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _kv = new();

    public IReadOnlyCollection<string> KeyNames => _keys.Keys.ToList();

    public Task CreateKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        _keys.TryAdd(keyName, RandomNumberGenerator.GetBytes(32));
        return Task.CompletedTask;
    }

    public Task RotateKeyAsync(string keyName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DestroyKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        _keys.TryRemove(keyName, out _);
        return Task.CompletedTask;
    }

    public async Task<DataKey> GenerateAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        return new DataKey(dek, await EncryptAsync(keyName, dek, cancellationToken));
    }

    public Task<byte[]> UnwrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default) =>
        DecryptAsync(keyName, wrapped, cancellationToken);

    private int _rewraps;

    public int Rewraps => _rewraps;

    /// <summary>Como no Vault: decifra e recifra sem devolver a DEK ao chamador.</summary>
    public async Task<string> RewrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _rewraps);
        return await EncryptAsync(keyName, await DecryptAsync(keyName, wrapped, cancellationToken), cancellationToken);
    }

    public Task<string> EncryptAsync(string keyName, byte[] plaintext, CancellationToken cancellationToken = default)
    {
        var key = _keys.GetOrAdd(keyName, _ => RandomNumberGenerator.GetBytes(32));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return Task.FromResult("vault:v1:" + Convert.ToBase64String([.. nonce, .. tag, .. cipher]));
    }

    public Task<byte[]> DecryptAsync(string keyName, string ciphertext, CancellationToken cancellationToken = default)
    {
        if (!_keys.TryGetValue(keyName, out var key))
        {
            throw new VaultException("Vault respondeu 400 (error).");
        }

        var raw = Convert.FromBase64String(ciphertext["vault:v1:".Length..]);
        var plain = new byte[raw.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(raw[..12], raw[28..], raw[12..28], plain);
        return Task.FromResult(plain);
    }

    public Task WriteAsync(string path, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        _kv[path] = new Dictionary<string, string>(values);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_kv.TryGetValue(path, out var v) ? v : null);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        _kv.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryObjectStorage : IObjectStorage
{
    public ConcurrentDictionary<string, bool> Buckets { get; } = new();

    public Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        Buckets[bucket] = true;
        return Task.CompletedTask;
    }

    public Task DeleteBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        Buckets.TryRemove(bucket, out _);
        foreach (var key in Objects.Keys.Where(k => k.StartsWith(bucket + "/", StringComparison.Ordinal)))
        {
            Objects.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>Objetos gravados, por "bucket/chave" — os testes de vazamento varrem estes bytes.</summary>
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();

    public Task PutAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!Buckets.ContainsKey(bucket))
        {
            throw new InvalidOperationException("Bucket inexistente.");
        }

        Objects[bucket + "/" + key] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Objects.TryGetValue(bucket + "/" + key, out var c) ? c.ToArray() : null);
}

/// <summary>
/// Sink do Serilog registrado no DI dos hosts (WebDefaults usa ReadFrom.Services): guarda mensagem, propriedades e
/// exceção de todo evento de log — os testes de vazamento procuram conteúdo sensível aqui.
/// </summary>
public sealed class CapturingLogSink : Serilog.Core.ILogEventSink
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public void Emit(Serilog.Events.LogEvent logEvent) =>
        Lines.Enqueue($"{logEvent.Level} {logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)} " +
            string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}")) + " " + logEvent.Exception);
}

public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    private int _failures;

    /// <summary>Faz os próximos N envios para este destinatário falharem (simula servidor SMTP fora do ar).</summary>
    public ConcurrentDictionary<string, int> FailuresFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Failures => _failures;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailuresFor.TryGetValue(message.To, out var left) && left > 0)
        {
            FailuresFor[message.To] = left - 1;
            Interlocked.Increment(ref _failures);
            throw new System.IO.IOException($"SMTP recusou a mensagem para {message.To}");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>API interna da plataforma simulada (o lado real é coberto pelos testes da plataforma).</summary>
public sealed class FakePlatformMemberships : IPlatformMemberships
{
    public ConcurrentDictionary<string, Guid> Invited { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<(Guid UserId, bool Active, bool Privileged)> Synced { get; } = new();

    public Task<PlatformInviteResult> InviteAsync(string tenantSlug, string email, string fullName, bool privileged, CancellationToken ct) =>
        Task.FromResult(email.EndsWith("@plataforma.local", StringComparison.OrdinalIgnoreCase)
            ? new PlatformInviteResult(null, PlatformAccount: true)
            : new PlatformInviteResult(Invited.GetOrAdd(email, _ => Guid.CreateVersion7()), PlatformAccount: false));

    public Task SyncAsync(string tenantSlug, Guid userId, bool active, bool privileged, CancellationToken ct)
    {
        Synced.Enqueue((userId, active, privileged));
        return Task.CompletedTask;
    }
}
