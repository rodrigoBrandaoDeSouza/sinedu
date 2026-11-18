using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Crypto;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed record AttachmentView(Guid Id, string OwnerKind, Guid OwnerId, string FileName, string ContentType, string? Description, long SizeBytes, DateTimeOffset CreatedAt, string CreatedByName);

public sealed record AttachmentContent(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Anexos de Saúde e Proteção: o arquivo é cifrado na API (envelope próprio) antes de ir ao bucket da escola.
/// O objeto no S3 é só ciphertext, com chave sem nome de arquivo. Download apenas via API.
/// </summary>
internal sealed class AttachmentService(
    ProtectedDbContext db, ProtectedCrypto crypto, IObjectStorage storage, TenantScope tenant, IAuditLog audit, TimeProvider clock)
{
    public const long MaxBytes = DocumentTypes.MaxBytes;

    public async Task<IReadOnlyList<AttachmentView>> ListAsync(Guid studentId, RecordKind? owner, Guid? ownerId, CancellationToken ct)
    {
        var query = db.Attachments.AsNoTracking().Where(a => a.StudentId == studentId && a.RemovedAt == null);
        if (owner is { } kind)
        {
            query = query.Where(a => a.OwnerKind == kind);
        }

        if (ownerId is { } id)
        {
            query = query.Where(a => a.OwnerId == id);
        }

        var list = new List<AttachmentView>();
        foreach (var a in await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct))
        {
            var meta = await MetadataAsync(a, ct);
            list.Add(new AttachmentView(a.Id, a.OwnerKind.ToString(), a.OwnerId, meta.FileName, meta.ContentType, meta.Description, a.SizeBytes, a.CreatedAt, a.CreatedByName));
        }

        return list;
    }

    public async Task<AttachmentView> UploadAsync(
        Guid studentId, RecordKind owner, Guid ownerId, string fileName, string? description, byte[] content, Actor actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (content.Length == 0 || content.Length > MaxBytes)
        {
            errors["file"] = ["Envie um arquivo de até 10 MB."];
        }

        var type = DocumentTypes.Detect(content);
        if (type is null)
        {
            errors["file"] = ["Formato não aceito. Envie PDF, PNG ou JPG."];
        }

        var desc = PayloadRules.Clean(description, 300, "description", errors);
        if (errors.Count > 0)
        {
            throw new ProtectedValidationException(errors);
        }

        var ownerExists = owner == RecordKind.Health
            ? await db.HealthRecords.AnyAsync(r => r.Id == ownerId && r.StudentId == studentId, ct)
            : await db.ProtectionRecords.AnyAsync(r => r.Id == ownerId && r.StudentId == studentId, ct);
        if (!ownerExists)
        {
            throw new ProtectedValidationException(new() { ["owner"] = ["Salve o registro antes de anexar documentos."] });
        }

        var id = Guid.CreateVersion7();
        var safeName = DocumentTypes.SafeFileName(fileName, type!);
        var file = await crypto.EncryptBytesAsync(content, crypto.AttachmentAad(studentId, id, "content"), ct);
        var meta = await crypto.EncryptAsync(new AttachmentMetadata(safeName, type!.ContentType, desc), crypto.AttachmentAad(studentId, id, "metadata"), ct);
        var objectKey = $"protected/{id:N}";
        await storage.PutAsync(tenant.Current.BucketName, objectKey, file.Ciphertext, ct);

        var now = clock.GetUtcNow();
        var attachment = new SecureAttachment
        {
            Id = id, StudentId = studentId, OwnerKind = owner, OwnerId = ownerId, ObjectKey = objectKey,
            Nonce = file.Nonce, Tag = file.Tag, WrappedKey = file.WrappedKey,
            MetadataCiphertext = meta.Ciphertext, MetadataNonce = meta.Nonce, MetadataTag = meta.Tag, MetadataWrappedKey = meta.WrappedKey,
            SizeBytes = content.Length, CiphertextSha256 = Convert.ToHexStringLower(SHA256.HashData(file.Ciphertext)),
            CreatedBy = actor.Id, CreatedByName = actor.Name, CreatedAt = now,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("protected_attachment_uploaded", ResourceType: "attachment", ResourceId: id.ToString(), StudentId: studentId), ct);
        return new AttachmentView(id, owner.ToString(), ownerId, safeName, type.ContentType, desc, content.Length, now, actor.Name);
    }

    public async Task<AttachmentContent?> DownloadAsync(Guid studentId, Guid attachmentId, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.StudentId == studentId && x.RemovedAt == null, ct);
        if (a is null)
        {
            return null;
        }

        var ciphertext = await storage.GetAsync(tenant.Current.BucketName, a.ObjectKey, ct);
        if (ciphertext is null || Convert.ToHexStringLower(SHA256.HashData(ciphertext)) != a.CiphertextSha256)
        {
            throw new InvalidOperationException("Anexo protegido indisponível ou alterado no armazenamento.");
        }

        var meta = await MetadataAsync(a, ct);
        var content = await crypto.DecryptBytesAsync(new EnvelopeCiphertext(ciphertext, a.Nonce, a.Tag, a.WrappedKey), crypto.AttachmentAad(studentId, a.Id, "content"), ct);
        await audit.WriteAsync(new AuditEntry("protected_attachment_downloaded", ResourceType: "attachment", ResourceId: a.Id.ToString(), StudentId: studentId), ct);
        return new AttachmentContent(meta.FileName, meta.ContentType, content);
    }

    /// <summary>Retira da lista (com motivo auditado). O objeto cifrado continua guardado: nada é apagado em silêncio.</summary>
    public async Task<bool> RemoveAsync(Guid studentId, Guid attachmentId, Actor actor, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.StudentId == studentId && x.RemovedAt == null, ct);
        if (a is null)
        {
            return false;
        }

        a.RemovedAt = clock.GetUtcNow();
        a.RemovedBy = actor.Id;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("protected_attachment_removed", ResourceType: "attachment", ResourceId: a.Id.ToString(), StudentId: studentId), ct);
        return true;
    }

    private Task<AttachmentMetadata> MetadataAsync(SecureAttachment a, CancellationToken ct) =>
        crypto.DecryptAsync<AttachmentMetadata>(
            new EnvelopeCiphertext(a.MetadataCiphertext, a.MetadataNonce, a.MetadataTag, a.MetadataWrappedKey),
            crypto.AttachmentAad(a.StudentId, a.Id, "metadata"), ct);
}
