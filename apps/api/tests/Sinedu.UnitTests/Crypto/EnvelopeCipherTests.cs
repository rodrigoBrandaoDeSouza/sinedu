using System.Security.Cryptography;
using System.Text;
using Sinedu.SharedKernel.Crypto;
using Sinedu.SharedKernel.Secrets;

namespace Sinedu.UnitTests.Crypto;

public class EnvelopeCipherTests
{
    private const string Key = "tenant-a";
    private const string Aad = "escola-a|aluno-1|health|registro-1|v1";
    private readonly FakeKeys _keys = new();
    private readonly EnvelopeCipher _cipher;

    public EnvelopeCipherTests() => _cipher = new EnvelopeCipher(_keys);

    [Fact]
    public async Task Cifra_e_decifra_com_o_mesmo_contexto()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"alergias":"dado fictício"}""");

        var envelope = await _cipher.EncryptAsync(Key, plaintext, Aad);
        var result = await _cipher.DecryptAsync(Key, envelope, Aad);

        Assert.Equal(plaintext, result);
        Assert.NotEqual(plaintext, envelope.Ciphertext);
    }

    [Fact]
    public async Task Ciphertext_movido_para_outro_aluno_nao_decifra()
    {
        var envelope = await _cipher.EncryptAsync(Key, "segredo"u8.ToArray(), Aad);

        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            _cipher.DecryptAsync(Key, envelope, "escola-a|aluno-2|health|registro-1|v1"));
    }

    [Fact]
    public async Task Ciphertext_adulterado_e_rejeitado()
    {
        var envelope = await _cipher.EncryptAsync(Key, "segredo"u8.ToArray(), Aad);
        envelope.Ciphertext[0] ^= 0xFF;

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _cipher.DecryptAsync(Key, envelope, Aad));
    }

    [Fact]
    public async Task Chave_de_outra_escola_nao_abre_o_envelope()
    {
        var envelope = await _cipher.EncryptAsync(Key, "segredo"u8.ToArray(), Aad);

        await Assert.ThrowsAnyAsync<Exception>(() => _cipher.DecryptAsync("tenant-b", envelope, Aad));
    }

    [Fact]
    public async Task Cada_registro_usa_uma_chave_de_dados_diferente()
    {
        var first = await _cipher.EncryptAsync(Key, "a"u8.ToArray(), Aad);
        var second = await _cipher.EncryptAsync(Key, "a"u8.ToArray(), Aad);

        Assert.NotEqual(first.WrappedKey, second.WrappedKey);
        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    /// <summary>Simula o Vault Transit: a DEK "embrulhada" só abre com a chave mestra de mesmo nome.</summary>
    private sealed class FakeKeys : IDataKeyProvider
    {
        private readonly Dictionary<string, byte[]> _masters = new()
        {
            ["tenant-a"] = RandomNumberGenerator.GetBytes(32),
            ["tenant-b"] = RandomNumberGenerator.GetBytes(32),
        };

        public Task<DataKey> GenerateAsync(string keyName, CancellationToken cancellationToken = default)
        {
            var dek = RandomNumberGenerator.GetBytes(32);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var wrapped = new byte[32];
            var tag = new byte[16];
            using var aes = new AesGcm(_masters[keyName], 16);
            aes.Encrypt(nonce, dek, wrapped, tag);
            return Task.FromResult(new DataKey(dek, Convert.ToBase64String([.. nonce, .. tag, .. wrapped])));
        }

        public Task<byte[]> UnwrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default)
        {
            var raw = Convert.FromBase64String(wrapped);
            var dek = new byte[32];
            using var aes = new AesGcm(_masters[keyName], 16);
            aes.Decrypt(raw[..12], raw[28..], raw[12..28], dek);
            return Task.FromResult(dek);
        }
    }
}
