using System.Text.Json;
using Sinedu.SharedKernel.Crypto;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.ProtectedRecords;

/// <summary>
/// Cifra e decifra conteúdo protegido com a chave DESTA escola (resolvida pelo host). O AAD amarra o ciphertext
/// ao contexto: copiar uma versão para outro aluno, registro, versão ou escola faz a decifragem falhar.
/// </summary>
internal sealed class ProtectedCrypto(IEnvelopeCipher cipher, TenantScope tenant)
{
    private string KeyName => tenant.Current.TransitKeyName;

    public static string VersionAad(Guid tenantId, Guid studentId, RecordKind kind, Guid recordId, int version) =>
        $"{tenantId:N}|{studentId:N}|{kind}|{recordId:N}|{version}";

    public static string AttachmentAad(Guid tenantId, Guid studentId, Guid attachmentId, string part) =>
        $"{tenantId:N}|{studentId:N}|attachment-{part}|{attachmentId:N}";

    public static string ExportAad(Guid tenantId, Guid studentId, Guid exportId) =>
        $"{tenantId:N}|{studentId:N}|export|{exportId:N}";

    public string Aad(Guid studentId, RecordKind kind, Guid recordId, int version) =>
        VersionAad(tenant.Current.Id, studentId, kind, recordId, version);

    public string AttachmentAad(Guid studentId, Guid attachmentId, string part) =>
        AttachmentAad(tenant.Current.Id, studentId, attachmentId, part);

    public string ExportAad(Guid studentId, Guid exportId) => ExportAad(tenant.Current.Id, studentId, exportId);

    public Task<EnvelopeCiphertext> EncryptBytesAsync(byte[] plaintext, string aad, CancellationToken ct) =>
        cipher.EncryptAsync(KeyName, plaintext, aad, ct);

    public Task<byte[]> DecryptBytesAsync(EnvelopeCiphertext envelope, string aad, CancellationToken ct) =>
        cipher.DecryptAsync(KeyName, envelope, aad, ct);

    public Task<EnvelopeCiphertext> EncryptAsync<T>(T value, string aad, CancellationToken ct) =>
        EncryptBytesAsync(JsonSerializer.SerializeToUtf8Bytes(value, PayloadJson.Options), aad, ct);

    public async Task<T> DecryptAsync<T>(EnvelopeCiphertext envelope, string aad, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await DecryptBytesAsync(envelope, aad, ct), PayloadJson.Options)
        ?? throw new InvalidOperationException("Conteúdo protegido vazio.");

    public static EnvelopeCiphertext Envelope(RecordVersion v) => new(v.Ciphertext, v.Nonce, v.Tag, v.WrappedKey);
}
