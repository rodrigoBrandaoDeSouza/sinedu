using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using OtpNet;
using Sinedu.SharedKernel.Secrets;

namespace Sinedu.Platform.Identity.Security;

/// <summary>Hash de senha com Argon2id (parâmetros mínimos recomendados pela OWASP).</summary>
public static class PasswordHasher
{
    private const int MemoryKiB = 19_456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static async Task<string> HashAsync(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = await ComputeAsync(password, salt, MemoryKiB, Iterations, Parallelism);
        return $"argon2id$m={MemoryKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static async Task<bool> VerifyAsync(string password, string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "argon2id")
        {
            return false;
        }

        var parameters = parts[1].Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = await ComputeAsync(password, salt, parameters["m"], parameters["t"], parameters["p"]);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static async Task<byte[]> ComputeAsync(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return await argon.GetBytesAsync(HashSize);
    }
}

public static class PasswordPolicy
{
    public const int MinLength = 12;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "senha1234567", "password1234", "qwertyuiop12", "escola123456", "sinedu123456", "aaaaaaaaaaaa",
    };

    /// <summary>Retorna a mensagem de erro, ou null se a senha for aceitável.</summary>
    public static string? Validate(string? password, string email)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"A senha precisa ter pelo menos {MinLength} caracteres.";
        }

        if (password.Length > 128)
        {
            return "A senha pode ter no máximo 128 caracteres.";
        }

        if (Common.Contains(password) || password.Distinct().Count() < 5)
        {
            return "Essa senha é muito comum. Escolha outra.";
        }

        var localPart = email.Split('@')[0];
        if (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            return "A senha não pode conter o seu e-mail.";
        }

        return null;
    }
}

/// <summary>Hash para tokens de alta entropia (convites, reset, códigos de recuperação).</summary>
public static class TokenHasher
{
    public static string NewToken() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(25)).ToLowerInvariant();

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant())));
}

/// <summary>TOTP (RFC 6238, 30 s, 6 dígitos). O segredo é cifrado pelo Vault antes de ir ao banco.</summary>
public sealed class TotpService(ISecretCipher cipher)
{
    public const string KeyName = "platform-secrets";
    private const string Issuer = "Sinedu";

    public static byte[] NewSecret() => KeyGeneration.GenerateRandomKey(20);

    public static string ToBase32(byte[] secret) => Base32Encoding.ToString(secret);

    public static string OtpAuthUri(byte[] secret, string email) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30";

    public Task<string> ProtectAsync(byte[] secret, CancellationToken ct) => cipher.EncryptAsync(KeyName, secret, ct);

    public Task<byte[]> UnprotectAsync(string encrypted, CancellationToken ct) => cipher.DecryptAsync(KeyName, encrypted, ct);

    /// <summary>Valida com janela de ±1 passo e rejeita passos já usados. Retorna o passo aceito.</summary>
    public static long? Verify(byte[] secret, string code, long? lastStep)
    {
        var normalized = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (normalized.Length != 6)
        {
            return null;
        }

        var totp = new Totp(secret);
        if (!totp.VerifyTotp(normalized, out var step, new VerificationWindow(1, 1)))
        {
            return null;
        }

        return lastStep is { } last && step <= last ? null : step;
    }

    public static IReadOnlyList<string> NewRecoveryCodes() =>
        Enumerable.Range(0, 10).Select(_ =>
        {
            var raw = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()[..10];
            return $"{raw[..5]}-{raw[5..]}";
        }).ToList();
}
