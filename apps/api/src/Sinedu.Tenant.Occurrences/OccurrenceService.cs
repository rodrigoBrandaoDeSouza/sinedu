using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Occurrences;

public sealed record Actor(Guid Id, string Name);

public sealed record OccurrenceInput(
    Guid StudentId, DateTimeOffset OccurredAt, Guid CategoryId, Guid? SeverityId, string? Location, string? Description,
    string? Involved, string? ActionsTaken, string? Referrals, string? Notes);

public sealed record OccurrenceListItem(
    Guid Id, Guid StudentId, string StudentName, string? StudentCode, Guid ClassroomId, string Classroom, DateTimeOffset OccurredAt,
    string Category, string? Severity, OccurrenceStatus Status, string CreatedByName, DateTimeOffset? RegisteredAt, int Version);

public sealed record RevisionInfo(int Version, string Action, string Reason, string ChangedByName, DateTimeOffset ChangedAt);

public sealed record AttachmentInfo(Guid Id, string FileName, string ContentType, long SizeBytes, string UploadedByName, DateTimeOffset UploadedAt);

public sealed record OccurrenceDetails(
    Guid Id, Guid StudentId, string StudentName, string? StudentCode, Guid ClassroomId, string Classroom, int? SchoolYear,
    DateTimeOffset OccurredAt, Guid CategoryId, string Category, Guid? SeverityId, string? Severity, string? Location, string Description,
    string? Involved, string? ActionsTaken, string? Referrals, string? Notes, OccurrenceStatus Status,
    string CreatedByName, DateTimeOffset CreatedAt, string? RegisteredByName, DateTimeOffset? RegisteredAt, DateTimeOffset? ConcludedAt,
    DateTimeOffset? CancelledAt, string? CancelReason, int Version, bool IsAuthor,
    IReadOnlyList<RevisionInfo> Revisions, IReadOnlyList<AttachmentInfo> Attachments);

/// <summary>Estado gravado em cada revisão (texto institucional; nada de Saúde/Proteção existe neste módulo).</summary>
public sealed record OccurrenceSnapshot(
    Guid StudentId, Guid ClassroomId, DateTimeOffset OccurredAt, string Category, string? Severity, string? Location, string Description,
    string? Involved, string? ActionsTaken, string? Referrals, string? Notes, OccurrenceStatus Status);

public sealed class OccurrenceValidationException(Dictionary<string, string[]> errors) : Exception("Dados inválidos.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class OccurrenceConflictException(string message) : Exception(message);

public sealed record OccurrenceFilter(Guid? StudentId, Guid? ClassroomId, OccurrenceStatus? Status, DateOnly? From, DateOnly? To, int Page = 1);

/// <summary>Contrato público para o módulo de comunicações (sem expor tabelas). Rascunhos não existem para ele.</summary>
public sealed record OccurrenceRef(Guid Id, Guid StudentId, OccurrenceStatus Status, DateTimeOffset OccurredAt);

public interface IOccurrenceDirectory
{
    Task<OccurrenceRef?> FindFormalizedAsync(Guid id, CancellationToken ct = default);
}

internal sealed class OccurrenceDirectory(OccurrencesDbContext db) : IOccurrenceDirectory
{
    public Task<OccurrenceRef?> FindFormalizedAsync(Guid id, CancellationToken ct = default) =>
        db.Occurrences.AsNoTracking()
            .Where(o => o.Id == id && o.Status != OccurrenceStatus.Rascunho)
            .Select(o => new OccurrenceRef(o.Id, o.StudentId, o.Status, o.OccurredAt))
            .FirstOrDefaultAsync(ct);
}

internal sealed class OccurrenceService(
    OccurrencesDbContext db, IStudentRegistry students, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock)
{
    public const int PageSize = 50;
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SaoPaulo).DateTime);

    // ---------- leitura ----------

    /// <summary>Rascunho só existe para quem o criou; para os demais, 404.</summary>
    private IQueryable<Occurrence> Visible(Guid viewer) =>
        db.Occurrences.AsNoTracking().Where(o => o.Status != OccurrenceStatus.Rascunho || o.CreatedBy == viewer);

    public async Task<(IReadOnlyList<OccurrenceListItem> Items, int Total)> ListAsync(OccurrenceFilter f, Guid viewer, CancellationToken ct)
    {
        var q = Visible(viewer);
        if (f.StudentId is { } sid) q = q.Where(o => o.StudentId == sid);
        if (f.ClassroomId is { } cid) q = q.Where(o => o.ClassroomId == cid);
        if (f.Status is { } st) q = q.Where(o => o.Status == st);
        if (f.From is { } from) q = q.Where(o => o.OccurredAt >= Start(from));
        if (f.To is { } to) q = q.Where(o => o.OccurredAt < Start(to.AddDays(1)));

        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(o => o.OccurredAt).Skip((Math.Max(f.Page, 1) - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return (await ProjectAsync(page, ct), total);
    }

    public async Task<OccurrenceDetails?> GetAsync(Guid id, Guid viewer, CancellationToken ct)
    {
        var o = await Visible(viewer).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null)
        {
            return null;
        }

        var found = await students.GetAsync([o.StudentId], ct);
        var student = found.Count > 0 ? found[0] : null;
        var room = (await classrooms.GetAsync([o.ClassroomId], ct)).GetValueOrDefault(o.ClassroomId);
        var category = await db.Categories.AsNoTracking().Where(c => c.Id == o.CategoryId).Select(c => c.Name).FirstAsync(ct);
        var severity = o.SeverityId is null ? null : await db.Severities.AsNoTracking().Where(s => s.Id == o.SeverityId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var revisions = await db.Revisions.AsNoTracking().Where(r => r.OccurrenceId == id).OrderByDescending(r => r.Version)
            .Select(r => new RevisionInfo(r.Version, r.Action, r.Reason, r.ChangedByName, r.ChangedAt)).ToListAsync(ct);
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.OccurrenceId == id && a.RemovedAt == null).OrderBy(a => a.UploadedAt)
            .Select(a => new AttachmentInfo(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByName, a.UploadedAt)).ToListAsync(ct);

        return new OccurrenceDetails(
            o.Id, o.StudentId, student?.FullName ?? "Aluno", student?.InstitutionalCode, o.ClassroomId, room?.Name ?? "—", room?.Year,
            o.OccurredAt, o.CategoryId, category, o.SeverityId, severity, o.Location, o.Description, o.Involved, o.ActionsTaken, o.Referrals, o.Notes,
            o.Status, o.CreatedByName, o.CreatedAt, o.RegisteredByName, o.RegisteredAt, o.ConcludedAt, o.CancelledAt, o.CancelReason, o.Version,
            o.CreatedBy == viewer, revisions, attachments);
    }

    public async Task<OccurrenceSnapshot?> GetRevisionAsync(Guid id, int version, Guid viewer, CancellationToken ct)
    {
        if (!await Visible(viewer).AnyAsync(o => o.Id == id, ct))
        {
            return null;
        }

        var r = await db.Revisions.AsNoTracking().FirstOrDefaultAsync(x => x.OccurrenceId == id && x.Version == version, ct);
        return r is null ? null : JsonSerializer.Deserialize<OccurrenceSnapshot>(r.Snapshot, Json);
    }

    // ---------- escrita ----------

    public async Task<Guid> CreateAsync(OccurrenceInput input, bool register, Actor actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var occurrence = new Occurrence
        {
            Id = Guid.CreateVersion7(), Description = "", CreatedBy = actor.Id, CreatedByName = actor.Name, CreatedAt = now, UpdatedAt = now,
            Status = OccurrenceStatus.Rascunho,
        };
        await ApplyAsync(occurrence, input, studentLocked: false, ct);
        db.Occurrences.Add(occurrence);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_created", ResourceType: "occurrence", ResourceId: occurrence.Id.ToString(), StudentId: occurrence.StudentId), ct);

        if (register)
        {
            await RegisterAsync(occurrence.Id, 0, actor, ct);
        }

        return occurrence.Id;
    }

    /// <summary>Rascunho: o autor edita livremente. Formalizada: exige justificativa e gera revisão.</summary>
    public async Task<bool> UpdateAsync(Guid id, OccurrenceInput input, string? reason, int expectedVersion, Actor actor, CancellationToken ct)
    {
        var o = await LoadForChangeAsync(id, actor, expectedVersion, ct);
        if (o is null)
        {
            return false;
        }

        if (o.Status is OccurrenceStatus.Concluida or OccurrenceStatus.Cancelada)
        {
            throw new OccurrenceConflictException("Ocorrência concluída ou cancelada não pode ser editada.");
        }

        var formal = o.Status != OccurrenceStatus.Rascunho;
        var why = formal ? RequireReason(reason, "reason", 5) : null;
        await ApplyAsync(o, input, studentLocked: formal, ct);
        o.UpdatedAt = clock.GetUtcNow();
        if (formal)
        {
            await AddRevisionAsync(o, "editada", why!, actor, ct);
        }

        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_updated", ResourceType: "occurrence", ResourceId: id.ToString(), StudentId: o.StudentId), ct);
        return true;
    }

    public async Task<bool> RegisterAsync(Guid id, int expectedVersion, Actor actor, CancellationToken ct)
    {
        var o = await LoadForChangeAsync(id, actor, expectedVersion, ct);
        if (o is null)
        {
            return false;
        }

        if (o.Status != OccurrenceStatus.Rascunho)
        {
            throw new OccurrenceConflictException("A ocorrência já foi registrada.");
        }

        var now = clock.GetUtcNow();
        o.Status = OccurrenceStatus.Registrada;
        o.RegisteredAt = now;
        o.RegisteredBy = actor.Id;
        o.RegisteredByName = actor.Name;
        o.UpdatedAt = now;
        await AddRevisionAsync(o, "registrada", "Registro inicial.", actor, ct);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_registered", ResourceType: "occurrence", ResourceId: id.ToString(), StudentId: o.StudentId), ct);
        return true;
    }

    public async Task<bool> ChangeStatusAsync(Guid id, OccurrenceStatus target, string? reason, int expectedVersion, Actor actor, CancellationToken ct)
    {
        var o = await LoadForChangeAsync(id, actor, expectedVersion, ct);
        if (o is null)
        {
            return false;
        }

        var allowed = (o.Status, target) switch
        {
            (OccurrenceStatus.Registrada, OccurrenceStatus.EmAcompanhamento) => true,
            (OccurrenceStatus.Registrada or OccurrenceStatus.EmAcompanhamento, OccurrenceStatus.Concluida) => true,
            (OccurrenceStatus.Concluida, OccurrenceStatus.EmAcompanhamento) => true,
            (OccurrenceStatus.Registrada or OccurrenceStatus.EmAcompanhamento or OccurrenceStatus.Concluida, OccurrenceStatus.Cancelada) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new OccurrenceConflictException("Esta mudança de situação não é permitida.");
        }

        var why = target switch
        {
            OccurrenceStatus.Cancelada => RequireReason(reason, "reason", 10),
            OccurrenceStatus.Concluida => RequireReason(reason, "reason", 5),
            _ => string.IsNullOrWhiteSpace(reason) ? "Em acompanhamento." : RequireReason(reason, "reason", 1),
        };

        var now = clock.GetUtcNow();
        o.Status = target;
        o.UpdatedAt = now;
        o.ConcludedAt = target == OccurrenceStatus.Concluida ? now : target == OccurrenceStatus.EmAcompanhamento ? null : o.ConcludedAt;
        if (target == OccurrenceStatus.Cancelada)
        {
            o.CancelledAt = now;
            o.CancelReason = why;
        }

        var action = target switch
        {
            OccurrenceStatus.EmAcompanhamento => "em_acompanhamento",
            OccurrenceStatus.Concluida => "concluida",
            _ => "cancelada",
        };
        await AddRevisionAsync(o, action, why, actor, ct);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_" + action, ResourceType: "occurrence", ResourceId: id.ToString(), StudentId: o.StudentId), ct);
        return true;
    }

    /// <summary>Só rascunho e só pelo autor. Formalizada nunca é excluída (P4).</summary>
    public async Task<bool> DeleteDraftAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var o = await db.Occurrences.FirstOrDefaultAsync(x => x.Id == id && x.CreatedBy == actor.Id, ct);
        if (o is null)
        {
            return false;
        }

        if (o.Status != OccurrenceStatus.Rascunho)
        {
            throw new OccurrenceConflictException("Ocorrência registrada não é excluída. Use cancelar, com justificativa.");
        }

        db.Occurrences.Remove(o);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_draft_deleted", ResourceType: "occurrence", ResourceId: id.ToString(), StudentId: o.StudentId), ct);
        return true;
    }

    // ---------- apoio ----------

    private async Task<Occurrence?> LoadForChangeAsync(Guid id, Actor actor, int expectedVersion, CancellationToken ct)
    {
        var o = await db.Occurrences.FirstOrDefaultAsync(x => x.Id == id && (x.Status != OccurrenceStatus.Rascunho || x.CreatedBy == actor.Id), ct);
        if (o is not null && o.Version != expectedVersion)
        {
            throw new OccurrenceConflictException("A ocorrência foi alterada por outra pessoa. Recarregue para ver a versão atual.");
        }

        return o;
    }

    private async Task ApplyAsync(Occurrence o, OccurrenceInput input, bool studentLocked, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (studentLocked && input.StudentId != o.StudentId)
        {
            errors["studentId"] = ["O aluno não muda depois do registro. Cancele e registre outra ocorrência."];
        }

        var now = clock.GetUtcNow();
        if (input.OccurredAt > now.AddMinutes(5))
        {
            errors["occurredAt"] = ["A data do fato não pode estar no futuro."];
        }

        var description = Clean(input.Description, 8000, "description", errors);
        if (description is null || description.Length < 10)
        {
            errors["description"] = ["Descreva o fato de forma objetiva (pelo menos 10 caracteres)."];
        }

        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId && (c.IsActive || c.Id == o.CategoryId), ct))
        {
            errors["categoryId"] = ["Escolha uma categoria."];
        }

        if (input.SeverityId is { } sev && !await db.Severities.AnyAsync(s => s.Id == sev, ct))
        {
            errors["severityId"] = ["Gravidade inválida."];
        }

        EnrollmentRef? enrollment = null;
        if (!errors.ContainsKey("studentId"))
        {
            if ((await students.GetAsync([input.StudentId], ct)).Count == 0)
            {
                errors["studentId"] = ["Aluno não encontrado nesta escola."];
            }
            else
            {
                // Turma do fato (spec §5.1): a matrícula que valia na data do acontecimento.
                enrollment = await students.GetEnrollmentOnAsync(input.StudentId, LocalDate(input.OccurredAt), ct);
                if (enrollment is null)
                {
                    errors["occurredAt"] = ["O aluno não tinha matrícula nesta escola na data do fato."];
                }
            }
        }

        var location = Clean(input.Location, 200, "location", errors);
        var involved = Clean(input.Involved, 2000, "involved", errors);
        var actions = Clean(input.ActionsTaken, 4000, "actionsTaken", errors);
        var referrals = Clean(input.Referrals, 4000, "referrals", errors);
        var notes = Clean(input.Notes, 4000, "notes", errors);
        if (errors.Count > 0)
        {
            throw new OccurrenceValidationException(errors);
        }

        o.StudentId = input.StudentId;
        o.EnrollmentId = enrollment!.EnrollmentId;
        o.ClassroomId = enrollment.ClassroomId;
        o.SchoolYearId = enrollment.SchoolYearId;
        o.OccurredAt = input.OccurredAt.ToUniversalTime();
        o.CategoryId = input.CategoryId;
        o.SeverityId = input.SeverityId;
        o.Location = location;
        o.Description = description!;
        o.Involved = involved;
        o.ActionsTaken = actions;
        o.Referrals = referrals;
        o.Notes = notes;
    }

    private async Task AddRevisionAsync(Occurrence o, string action, string reason, Actor actor, CancellationToken ct)
    {
        o.Version++;
        var category = await db.Categories.AsNoTracking().Where(c => c.Id == o.CategoryId).Select(c => c.Name).FirstAsync(ct);
        var severity = o.SeverityId is null ? null : await db.Severities.AsNoTracking().Where(s => s.Id == o.SeverityId).Select(s => s.Name).FirstAsync(ct);
        var snapshot = new OccurrenceSnapshot(
            o.StudentId, o.ClassroomId, o.OccurredAt, category, severity, o.Location, o.Description, o.Involved, o.ActionsTaken, o.Referrals, o.Notes, o.Status);
        db.Revisions.Add(new OccurrenceRevision
        {
            Id = Guid.CreateVersion7(), OccurrenceId = o.Id, Version = o.Version, Action = action, Reason = reason,
            Snapshot = JsonSerializer.Serialize(snapshot, Json), ChangedBy = actor.Id, ChangedByName = actor.Name, ChangedAt = clock.GetUtcNow(),
        });
    }

    private async Task<IReadOnlyList<OccurrenceListItem>> ProjectAsync(List<Occurrence> page, CancellationToken ct)
    {
        var people = (await students.GetAsync(page.Select(o => o.StudentId).Distinct().ToList(), ct)).ToDictionary(s => s.Id);
        var rooms = await classrooms.GetAsync(page.Select(o => o.ClassroomId), ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var severities = await db.Severities.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return page.Select(o => new OccurrenceListItem(
            o.Id, o.StudentId, people.GetValueOrDefault(o.StudentId)?.FullName ?? "Aluno", people.GetValueOrDefault(o.StudentId)?.InstitutionalCode,
            o.ClassroomId, rooms.GetValueOrDefault(o.ClassroomId)?.Name ?? "—", o.OccurredAt, categories.GetValueOrDefault(o.CategoryId, "—"),
            o.SeverityId is { } s ? severities.GetValueOrDefault(s) : null, o.Status, o.CreatedByName, o.RegisteredAt, o.Version)).ToList();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new OccurrenceConflictException("A ocorrência foi alterada por outra pessoa. Recarregue para ver a versão atual.");
        }
    }

    private static DateTimeOffset Start(DateOnly localDate)
    {
        var local = localDate.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, SaoPaulo.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string RequireReason(string? reason, string field, int min)
    {
        var errors = new Dictionary<string, string[]>();
        var r = Clean(reason, 1000, field, errors);
        if (r is null || r.Length < min)
        {
            errors[field] = [min >= 10 ? "Explique o motivo (pelo menos 10 caracteres)." : "Explique o motivo da alteração."];
        }

        return errors.Count > 0 ? throw new OccurrenceValidationException(errors) : r!;
    }

    internal static string? Clean(string? value, int max, string field, Dictionary<string, string[]> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (v.Length > max)
        {
            errors[field] = [$"Use no máximo {max} caracteres."];
        }

        return v;
    }
}
