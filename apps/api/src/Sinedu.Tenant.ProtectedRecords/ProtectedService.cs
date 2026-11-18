using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed record Actor(Guid Id, string Name);

public sealed record VersionInfo(int Version, DateTimeOffset CreatedAt, string CreatedByName);

public sealed record HealthView(Guid Id, int Version, DateOnly? ReviewDueOn, HealthData Data, DateTimeOffset UpdatedAt, string UpdatedByName);

public sealed record HealthVersionView(int Version, DateTimeOffset CreatedAt, string CreatedByName, string Reason, HealthData Data);

/// <summary>Situação exibida: "Vencida" é calculada (vigente com validade passada), nunca gravada por rotina automática.</summary>
public sealed record ProtectionView(
    Guid Id, ProtectionKind Kind, string Status, DateOnly? ValidUntil, int Version, ProtectionData Data, DateTimeOffset UpdatedAt, string UpdatedByName);

public sealed record ProtectionVersionView(int Version, DateTimeOffset CreatedAt, string CreatedByName, string Reason, ProtectionData Data);

public sealed record PendingItem(Guid StudentId, string StudentName, string? Classroom, string Type, Guid RecordId, ProtectionKind? Kind, DateOnly DueOn);

public sealed class ProtectedValidationException(Dictionary<string, string[]> errors) : Exception("Dados inválidos.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ProtectedConflictException() : Exception("O registro foi alterado por outra pessoa. Recarregue para ver a versão atual.");

/// <summary>Regras de Saúde e Proteção/Sigilo. Toda leitura de conteúdo e toda alteração é auditada, sem conteúdo.</summary>
internal sealed class ProtectedService(
    ProtectedDbContext db, ProtectedCrypto crypto, IStudentRegistry students, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock)
{
    public const int DueSoonDays = 30;
    private const int HealthSchema = 1;
    private const int ProtectionSchema = 1;

    /// <summary>O aluno existe NESTA escola? (cada escola tem banco próprio). Senão a rota responde 404.</summary>
    public async Task<bool> StudentExistsAsync(Guid studentId, CancellationToken ct)
    {
        if ((await students.GetAsync([studentId], ct)).Count == 1)
        {
            return true;
        }

        await audit.WriteAsync(new AuditEntry("protected_access_denied", AuditResult.Denied, "student", studentId.ToString(), StudentId: studentId), ct);
        return false;
    }

    // ---------- Saúde ----------

    public async Task<HealthView?> GetHealthAsync(Guid studentId, CancellationToken ct)
    {
        var record = await db.HealthRecords.AsNoTracking().FirstOrDefaultAsync(r => r.StudentId == studentId, ct);
        if (record is null)
        {
            await audit.WriteAsync(new AuditEntry("health_viewed", ResourceType: "health", StudentId: studentId), ct);
            return null;
        }

        var version = await db.Versions.AsNoTracking()
            .FirstAsync(v => v.RecordKind == RecordKind.Health && v.RecordId == record.Id && v.Version == record.CurrentVersion, ct);
        var payload = await DecryptAsync<HealthData>(version, ct);
        await audit.WriteAsync(new AuditEntry("health_viewed", ResourceType: "health", ResourceId: record.Id.ToString(), StudentId: studentId), ct);
        return new HealthView(record.Id, record.CurrentVersion, record.ReviewDueOn, payload.Data, version.CreatedAt, version.CreatedByName);
    }

    public async Task<HealthView> SaveHealthAsync(
        Guid studentId, HealthData data, DateOnly? reviewDueOn, string? reason, int? expectedVersion, Actor actor, CancellationToken ct, string action = "health_updated")
    {
        var errors = new Dictionary<string, string[]>();
        var clean = PayloadRules.Normalize(data, errors);
        var why = PayloadRules.Reason(reason, errors);
        if (errors.Count > 0)
        {
            throw new ProtectedValidationException(errors);
        }

        var now = clock.GetUtcNow();
        var record = await db.HealthRecords.FirstOrDefaultAsync(r => r.StudentId == studentId, ct);
        if (record is null)
        {
            if (expectedVersion is > 0)
            {
                throw new ProtectedConflictException();
            }

            record = new HealthRecord { Id = Guid.CreateVersion7(), StudentId = studentId, CreatedAt = now };
            db.HealthRecords.Add(record);
        }
        else if (expectedVersion != record.CurrentVersion)
        {
            throw new ProtectedConflictException();
        }

        record.CurrentVersion++;
        record.ReviewDueOn = reviewDueOn;
        record.UpdatedAt = now;
        db.Versions.Add(await NewVersionAsync(RecordKind.Health, record.Id, studentId, record.CurrentVersion, HealthSchema, clean, why!, actor, now, ct));
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry(action, ResourceType: "health", ResourceId: record.Id.ToString(), StudentId: studentId), ct);
        return new HealthView(record.Id, record.CurrentVersion, record.ReviewDueOn, clean, now, actor.Name);
    }

    // ---------- Proteção/Sigilo ----------

    public async Task<IReadOnlyList<ProtectionView>> ListProtectionAsync(Guid studentId, CancellationToken ct)
    {
        var records = await db.ProtectionRecords.AsNoTracking().Where(r => r.StudentId == studentId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var ids = records.Select(r => r.Id).ToList();
        var versions = await db.Versions.AsNoTracking()
            .Where(v => v.RecordKind == RecordKind.Protection && ids.Contains(v.RecordId))
            .ToListAsync(ct);

        var result = new List<ProtectionView>();
        foreach (var r in records)
        {
            var v = versions.First(x => x.RecordId == r.Id && x.Version == r.CurrentVersion);
            var payload = await DecryptAsync<ProtectionData>(v, ct);
            result.Add(View(r, payload.Data, v));
        }

        await audit.WriteAsync(new AuditEntry("protection_viewed", ResourceType: "protection", StudentId: studentId), ct);
        return result;
    }

    public async Task<ProtectionView> CreateProtectionAsync(
        Guid studentId, ProtectionKind kind, DateOnly? validUntil, ProtectionData data, string? reason, Actor actor, CancellationToken ct)
    {
        var (clean, why) = Validate(data, reason, validUntil);
        var now = clock.GetUtcNow();
        var record = new ProtectionRecord
        {
            Id = Guid.CreateVersion7(), StudentId = studentId, Kind = kind, Status = ProtectionStatus.Vigente,
            ValidUntil = validUntil, CurrentVersion = 1, CreatedAt = now, UpdatedAt = now,
        };
        db.ProtectionRecords.Add(record);
        var version = await NewVersionAsync(RecordKind.Protection, record.Id, studentId, 1, ProtectionSchema, clean, why, actor, now, ct);
        db.Versions.Add(version);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("protection_created", ResourceType: "protection", ResourceId: record.Id.ToString(), StudentId: studentId), ct);
        return View(record, clean, version);
    }

    public async Task<ProtectionView?> UpdateProtectionAsync(
        Guid studentId, Guid recordId, ProtectionKind kind, DateOnly? validUntil, ProtectionData data, string? reason, int expectedVersion, Actor actor, CancellationToken ct)
    {
        var record = await db.ProtectionRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.StudentId == studentId, ct);
        if (record is null)
        {
            return null;
        }

        if (record.Status == ProtectionStatus.Revogada)
        {
            throw new ProtectedValidationException(new() { ["status"] = ["Registro revogado não pode ser alterado. Crie um novo registro."] });
        }

        if (record.CurrentVersion != expectedVersion)
        {
            throw new ProtectedConflictException();
        }

        var (clean, why) = Validate(data, reason, validUntil);
        var now = clock.GetUtcNow();
        record.Kind = kind;
        record.ValidUntil = validUntil;
        record.CurrentVersion++;
        record.UpdatedAt = now;
        var version = await NewVersionAsync(RecordKind.Protection, record.Id, studentId, record.CurrentVersion, ProtectionSchema, clean, why, actor, now, ct);
        db.Versions.Add(version);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("protection_updated", ResourceType: "protection", ResourceId: record.Id.ToString(), StudentId: studentId), ct);
        return View(record, clean, version);
    }

    /// <summary>Revogação: nova versão com o motivo; o conteúdo anterior continua no histórico.</summary>
    public async Task<ProtectionView?> RevokeProtectionAsync(Guid studentId, Guid recordId, string? reason, int expectedVersion, Actor actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var why = PayloadRules.Reason(reason, errors);
        if (errors.Count > 0)
        {
            throw new ProtectedValidationException(errors);
        }

        var record = await db.ProtectionRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.StudentId == studentId, ct);
        if (record is null)
        {
            return null;
        }

        if (record.CurrentVersion != expectedVersion || record.Status == ProtectionStatus.Revogada)
        {
            throw new ProtectedConflictException();
        }

        var current = await db.Versions.AsNoTracking()
            .FirstAsync(v => v.RecordKind == RecordKind.Protection && v.RecordId == record.Id && v.Version == record.CurrentVersion, ct);
        var data = (await DecryptAsync<ProtectionData>(current, ct)).Data;
        var now = clock.GetUtcNow();
        record.Status = ProtectionStatus.Revogada;
        record.RevokedAt = now;
        record.CurrentVersion++;
        record.UpdatedAt = now;
        var version = await NewVersionAsync(RecordKind.Protection, record.Id, studentId, record.CurrentVersion, ProtectionSchema, data, why!, actor, now, ct);
        db.Versions.Add(version);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("protection_revoked", ResourceType: "protection", ResourceId: record.Id.ToString(), StudentId: studentId), ct);
        return View(record, data, version);
    }

    // ---------- Histórico ----------

    public async Task<IReadOnlyList<VersionInfo>?> ListVersionsAsync(Guid studentId, RecordKind kind, Guid? recordId, CancellationToken ct)
    {
        var id = await ResolveRecordAsync(studentId, kind, recordId, ct);
        if (id is null)
        {
            return null;
        }

        return await db.Versions.AsNoTracking()
            .Where(v => v.RecordKind == kind && v.RecordId == id && v.StudentId == studentId)
            .OrderByDescending(v => v.Version)
            .Select(v => new VersionInfo(v.Version, v.CreatedAt, v.CreatedByName))
            .ToListAsync(ct);
    }

    public async Task<(string Reason, T Data, RecordVersion Version)?> GetVersionAsync<T>(Guid studentId, RecordKind kind, Guid? recordId, int version, CancellationToken ct)
    {
        var id = await ResolveRecordAsync(studentId, kind, recordId, ct);
        if (id is null)
        {
            return null;
        }

        var v = await db.Versions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RecordKind == kind && x.RecordId == id && x.StudentId == studentId && x.Version == version, ct);
        if (v is null)
        {
            return null;
        }

        var payload = await DecryptAsync<T>(v, ct);
        await audit.WriteAsync(new AuditEntry(kind == RecordKind.Health ? "health_version_viewed" : "protection_version_viewed",
            ResourceType: kind == RecordKind.Health ? "health" : "protection", ResourceId: id.ToString(), StudentId: studentId), ct);
        return (payload.Reason, payload.Data, v);
    }

    // ---------- Pendências (F4-04) ----------

    /// <summary>Revisões de Saúde e validades de Proteção que vencem em até 30 dias ou já venceram. Sem conteúdo.</summary>
    public async Task<IReadOnlyList<PendingItem>> PendingAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var limit = today.AddDays(DueSoonDays);
        var health = await db.HealthRecords.AsNoTracking().Where(r => r.ReviewDueOn != null && r.ReviewDueOn <= limit).ToListAsync(ct);
        var protection = await db.ProtectionRecords.AsNoTracking()
            .Where(r => r.Status == ProtectionStatus.Vigente && r.ValidUntil != null && r.ValidUntil <= limit)
            .ToListAsync(ct);

        var ids = health.Select(h => h.StudentId).Concat(protection.Select(p => p.StudentId)).Distinct().ToList();
        var snapshots = (await students.GetAsync(ids, ct)).ToDictionary(s => s.Id);
        var rooms = await classrooms.GetAsync(snapshots.Values.Where(s => s.ActiveClassroomId != null).Select(s => s.ActiveClassroomId!.Value), ct);
        string Name(Guid id) => snapshots.TryGetValue(id, out var s) ? s.FullName : "Aluno";
        string? Room(Guid id) => snapshots.TryGetValue(id, out var s) && s.ActiveClassroomId is { } c && rooms.TryGetValue(c, out var r) ? r.Name : null;

        var items = health
            .Select(h => new PendingItem(h.StudentId, Name(h.StudentId), Room(h.StudentId), "health_review", h.Id, null, h.ReviewDueOn!.Value))
            .Concat(protection.Select(p => new PendingItem(
                p.StudentId, Name(p.StudentId), Room(p.StudentId), p.ValidUntil < today ? "protection_expired" : "protection_expiring", p.Id, p.Kind, p.ValidUntil!.Value)))
            .OrderBy(i => i.DueOn)
            .ToList();
        await audit.WriteAsync(new AuditEntry("protected_pending_viewed", ResourceType: "protected"), ct);
        return items;
    }

    // ---------- apoio ----------

    internal async Task<VersionPayload<T>> DecryptAsync<T>(RecordVersion v, CancellationToken ct) =>
        await crypto.DecryptAsync<VersionPayload<T>>(ProtectedCrypto.Envelope(v), crypto.Aad(v.StudentId, v.RecordKind, v.RecordId, v.Version), ct);

    private async Task<RecordVersion> NewVersionAsync<T>(
        RecordKind kind, Guid recordId, Guid studentId, int version, int schema, T data, string reason, Actor actor, DateTimeOffset now, CancellationToken ct)
    {
        var envelope = await crypto.EncryptAsync(new VersionPayload<T>(data, reason), crypto.Aad(studentId, kind, recordId, version), ct);
        return new RecordVersion
        {
            Id = Guid.CreateVersion7(), RecordKind = kind, RecordId = recordId, StudentId = studentId, Version = version, PayloadSchemaVersion = schema,
            Ciphertext = envelope.Ciphertext, Nonce = envelope.Nonce, Tag = envelope.Tag, WrappedKey = envelope.WrappedKey,
            CreatedBy = actor.Id, CreatedByName = actor.Name, CreatedAt = now,
        };
    }

    private async Task<Guid?> ResolveRecordAsync(Guid studentId, RecordKind kind, Guid? recordId, CancellationToken ct) =>
        kind == RecordKind.Health
            ? await db.HealthRecords.Where(r => r.StudentId == studentId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)
            : await db.ProtectionRecords.Where(r => r.StudentId == studentId && r.Id == recordId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);

    private static (ProtectionData Data, string Reason) Validate(ProtectionData data, string? reason, DateOnly? validUntil)
    {
        var errors = new Dictionary<string, string[]>();
        var clean = PayloadRules.Normalize(data, errors);
        var why = PayloadRules.Reason(reason, errors);
        if (validUntil is { } until && clean.ValidFrom is { } from && until < from)
        {
            errors["validUntil"] = ["A validade termina antes de começar."];
        }

        if (errors.Count > 0)
        {
            throw new ProtectedValidationException(errors);
        }

        return (clean, why!);
    }

    private ProtectionView View(ProtectionRecord r, ProtectionData data, RecordVersion v)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var status = r.Status == ProtectionStatus.Revogada ? "Revogada" : r.ValidUntil is { } until && until < today ? "Vencida" : "Vigente";
        return new ProtectionView(r.Id, r.Kind, status, r.ValidUntil, r.CurrentVersion, data, v.CreatedAt, v.CreatedByName);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concorrência (xmin) ou corrida na criação da ficha única: nunca sobrescreve em silêncio.
            throw new ProtectedConflictException();
        }
    }
}
