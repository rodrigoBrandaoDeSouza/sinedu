using System.Security.Cryptography;
using System.Text;
using Sinedu.SharedKernel.Secrets;

namespace Sinedu.SharedKernel.Crypto;

/// <summary>Resultado cifrado de um registro sensível. Nada aqui é legível sem a chave da escola no Vault.</summary>
public sealed record EnvelopeCiphertext(byte[] Ciphertext, byte[] Nonce, byte[] Tag, string WrappedKey);

public interface IEnvelopeCipher
{
    Task<EnvelopeCiphertext> EncryptAsync(string keyName, byte[] plaintext, string associatedData, CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(string keyName, EnvelopeCiphertext envelope, string associatedData, CancellationToken cancellationToken = default);
}

/// <summary>
/// Criptografia por envelope: uma DEK (AES-256) por registro, AES-GCM com dados associados (AAD)
/// que amarram o ciphertext ao contexto (escola, aluno, tipo, registro, versão). Trocar o ciphertext
/// de lugar faz a decifragem falhar.
/// </summary>
public sealed class EnvelopeCipher(IDataKeyProvider keys) : IEnvelopeCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public async Task<EnvelopeCiphertext> EncryptAsync(
        string keyName, byte[] plaintext, string associatedData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var dataKey = await keys.GenerateAsync(keyName, cancellationToken);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(dataKey.Plaintext, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(associatedData));
            return new EnvelopeCiphertext(ciphertext, nonce, tag, dataKey.Wrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey.Plaintext);
        }
    }

    public async Task<byte[]> DecryptAsync(
        string keyName, EnvelopeCiphertext envelope, string associatedData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var key = await keys.UnwrapAsync(keyName, envelope.WrappedKey, cancellationToken);
        try
        {
            var plaintext = new byte[envelope.Ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, Encoding.UTF8.GetBytes(associatedData));
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
