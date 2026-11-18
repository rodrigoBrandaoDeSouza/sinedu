using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sinedu.SharedKernel.Secrets;

public sealed class VaultOptions
{
    public const string Section = "Vault";

    public Uri Address { get; set; } = new("http://localhost:8200");

    /// <summary>Token de acesso. Em produção, obtido por AppRole e injetado por variável de ambiente.</summary>
    public string Token { get; set; } = "";

    public string TransitMount { get; set; } = "transit";

    public string KvMount { get; set; } = "secret";
}

/// <summary>
/// Cliente mínimo da API HTTP do Vault (Transit + KV v2). Implementa as abstrações de segredos;
/// o que cada host pode efetivamente fazer é limitado pela política do token no Vault.
/// </summary>
public sealed class VaultClient(HttpClient http, IOptions<VaultOptions> options)
    : IDataKeyProvider, IKeyManagement, ISecretCipher, ISecretStore
{
    private readonly VaultOptions _options = options.Value;

    public async Task<DataKey> GenerateAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync($"{_options.TransitMount}/datakey/plaintext/{Escape(keyName)}", new { bits = 256 }, cancellationToken);
        return new DataKey(
            Convert.FromBase64String(data.GetProperty("plaintext").GetString()!),
            data.GetProperty("ciphertext").GetString()!);
    }

    public async Task<byte[]> UnwrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync($"{_options.TransitMount}/decrypt/{Escape(keyName)}", new { ciphertext = wrapped }, cancellationToken);
        return Convert.FromBase64String(data.GetProperty("plaintext").GetString()!);
    }

    public async Task<string> RewrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync($"{_options.TransitMount}/rewrap/{Escape(keyName)}", new { ciphertext = wrapped }, cancellationToken);
        return data.GetProperty("ciphertext").GetString()!;
    }

    public async Task<string> EncryptAsync(string keyName, byte[] plaintext, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(
            $"{_options.TransitMount}/encrypt/{Escape(keyName)}",
            new { plaintext = Convert.ToBase64String(plaintext) },
            cancellationToken);
        return data.GetProperty("ciphertext").GetString()!;
    }

    public Task<byte[]> DecryptAsync(string keyName, string ciphertext, CancellationToken cancellationToken = default) =>
        UnwrapAsync(keyName, ciphertext, cancellationToken);

    public async Task CreateKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Post, $"{_options.TransitMount}/keys/{Escape(keyName)}", new { type = "aes256-gcm96" });
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RotateKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Post, $"{_options.TransitMount}/keys/{Escape(keyName)}/rotate", null);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DestroyKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        using (var config = NewRequest(HttpMethod.Post, $"{_options.TransitMount}/keys/{Escape(keyName)}/config", new { deletion_allowed = true }))
        using (var response = await http.SendAsync(config, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }

        using var delete = NewRequest(HttpMethod.Delete, $"{_options.TransitMount}/keys/{Escape(keyName)}", null);
        using var deleteResponse = await http.SendAsync(delete, cancellationToken);
        await EnsureSuccessAsync(deleteResponse, cancellationToken);
    }

    public async Task WriteAsync(string path, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        await PostAsync($"{_options.KvMount}/data/{path}", new { data = values }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, $"{_options.KvMount}/data/{path}", null);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<VaultResponse>(cancellationToken);
        var data = body!.Data.GetProperty("data");
        return data.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Delete, $"{_options.KvMount}/metadata/{path}", null);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, path, body);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var result = await response.Content.ReadFromJsonAsync<VaultResponse>(cancellationToken);
        return result!.Data;
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.Address, "v1/" + path));
        request.Headers.Add("X-Vault-Token", _options.Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Erros do Vault nunca carregam conteúdo de segredos; só o status vai para a exceção.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var reason = body.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ? "permission denied" : "error";
            throw new VaultException($"Vault respondeu {(int)response.StatusCode} ({reason}).");
        }
    }

    private static string Escape(string name) => Uri.EscapeDataString(name);

    private sealed record VaultResponse([property: JsonPropertyName("data")] JsonElement Data);
}

public sealed class VaultException(string message) : Exception(message);
