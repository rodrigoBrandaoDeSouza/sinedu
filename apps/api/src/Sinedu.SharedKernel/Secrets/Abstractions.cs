namespace Sinedu.SharedKernel.Secrets;

/// <summary>Chave de dados gerada pelo Vault: texto claro (só em memória) + versão cifrada pela chave da escola.</summary>
public sealed record DataKey(byte[] Plaintext, string Wrapped);

/// <summary>
/// Envelope encryption para a aplicação da escola: gera e decifra DEKs com a chave da escola.
/// Nunca cria, rotaciona ou destrói chaves (isso é do <see cref="IKeyManagement"/>).
/// </summary>
public interface IDataKeyProvider
{
    Task<DataKey> GenerateAsync(string keyName, CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default);

    /// <summary>Recifra uma DEK com a versão mais nova da chave da escola, sem expor a DEK (Vault Transit rewrap).</summary>
    Task<string> RewrapAsync(string keyName, string wrapped, CancellationToken cancellationToken = default);
}

/// <summary>Gestão do ciclo de vida das chaves das escolas. Usado só pela plataforma; não decifra dados.</summary>
public interface IKeyManagement
{
    Task CreateKeyAsync(string keyName, CancellationToken cancellationToken = default);

    Task RotateKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>Destrói a chave (crypto-shredding). Irreversível.</summary>
    Task DestroyKeyAsync(string keyName, CancellationToken cancellationToken = default);
}

/// <summary>Cifra segredos pequenos (ex.: segredo TOTP) com uma chave nomeada, sem expor a chave.</summary>
public interface ISecretCipher
{
    Task<string> EncryptAsync(string keyName, byte[] plaintext, CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(string keyName, string ciphertext, CancellationToken cancellationToken = default);
}

/// <summary>Armazenamento de segredos chave/valor (credenciais de banco das escolas).</summary>
public interface ISecretStore
{
    Task WriteAsync(string path, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
