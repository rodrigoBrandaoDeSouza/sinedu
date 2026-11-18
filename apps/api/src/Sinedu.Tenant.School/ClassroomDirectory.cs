using Microsoft.EntityFrameworkCore;

namespace Sinedu.Tenant.School;

public sealed record ClassroomRef(Guid Id, string Name, Guid SchoolYearId, int Year, bool ActiveYear);

public sealed record ActiveYearRef(Guid Id, int Year, DateOnly StartsOn);

/// <summary>Contrato público do módulo School para outros módulos (Students, Imports), sem expor tabelas.</summary>
public interface IClassroomDirectory
{
    Task<IReadOnlyDictionary<Guid, ClassroomRef>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    Task<ClassroomRef?> FindAsync(Guid id, CancellationToken ct = default);

    Task<ActiveYearRef?> GetActiveYearAsync(CancellationToken ct = default);

    /// <summary>Turmas do ano indexadas pela chave (série + turma + turno).</summary>
    Task<IReadOnlyDictionary<ClassroomKey, Guid>> GetByKeyAsync(Guid yearId, CancellationToken ct = default);

    /// <summary>Cria série e turma se ainda não existirem (usado na confirmação de importação).</summary>
    Task<Guid> EnsureAsync(Guid yearId, ClassroomKey key, string? externalCode, CancellationToken ct = default);
}

internal sealed class ClassroomDirectory(SchoolDbContext db) : IClassroomDirectory
{
    public async Task<IReadOnlyDictionary<Guid, ClassroomRef>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return await db.Classrooms.AsNoTracking()
            .Where(c => list.Contains(c.Id))
            .Select(c => new ClassroomRef(c.Id, Classroom.DisplayName(c.Grade!.ShortName, c.Section, c.Shift), c.SchoolYearId, c.SchoolYear!.Year,
                c.SchoolYear.Status == SchoolYearStatus.Active))
            .ToDictionaryAsync(c => c.Id, ct);
    }

    public async Task<ClassroomRef?> FindAsync(Guid id, CancellationToken ct = default) =>
        (await GetAsync([id], ct)).GetValueOrDefault(id);

    public async Task<ActiveYearRef?> GetActiveYearAsync(CancellationToken ct = default) =>
        await db.Years.AsNoTracking().Where(y => y.Status == SchoolYearStatus.Active)
            .Select(y => new ActiveYearRef(y.Id, y.Year, y.StartsOn)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<ClassroomKey, Guid>> GetByKeyAsync(Guid yearId, CancellationToken ct = default)
    {
        var rows = await db.Classrooms.AsNoTracking().Where(c => c.SchoolYearId == yearId)
            .Select(c => new { c.Id, c.Grade!.ShortName, c.Grade.Stage, c.Section, c.Shift })
            .ToListAsync(ct);
        return rows.ToDictionary(r => new ClassroomKey(r.ShortName, r.Stage, r.Section, r.Shift), r => r.Id);
    }

    public async Task<Guid> EnsureAsync(Guid yearId, ClassroomKey key, string? externalCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var grade = await db.Grades.FirstOrDefaultAsync(g => g.Stage == key.Stage && g.ShortName == key.GradeShort, ct);
        if (grade is null)
        {
            grade = new Grade
            {
                Id = Guid.CreateVersion7(), ShortName = key.GradeShort, Stage = key.Stage, Name = ClassroomLabel.GradeName(key), SortOrder = ClassroomLabel.GradeOrder(key),
            };
            db.Grades.Add(grade);
        }

        var classroom = await db.Classrooms.FirstOrDefaultAsync(c => c.SchoolYearId == yearId && c.GradeId == grade.Id && c.Section == key.Section && c.Shift == key.Shift, ct);
        if (classroom is null)
        {
            classroom = new Classroom
            {
                Id = Guid.CreateVersion7(), SchoolYearId = yearId, GradeId = grade.Id, Section = key.Section, Shift = key.Shift, Status = ClassroomStatus.Active,
                ExternalCode = externalCode,
            };
            db.Classrooms.Add(classroom);
        }
        else if (classroom.ExternalCode is null && externalCode is not null)
        {
            classroom.ExternalCode = externalCode;
        }

        await db.SaveChangesAsync(ct);
        return classroom.Id;
    }
}

/// <summary>Cabeçalho institucional (atas, e-mails). Contrato público do módulo School.</summary>
public sealed record SchoolHeader(string Name, string? InstitutionalCode, string? Address, string? Phone, string? Email);

public interface ISchoolProfileReader
{
    Task<SchoolHeader> GetAsync(CancellationToken ct = default);
}

internal sealed class SchoolProfileReader(SchoolDbContext db, Sinedu.SharedKernel.Tenancy.TenantScope tenant) : ISchoolProfileReader
{
    public async Task<SchoolHeader> GetAsync(CancellationToken ct = default)
    {
        var p = await db.Profile.AsNoTracking().FirstOrDefaultAsync(ct);
        return p is null
            ? new SchoolHeader(tenant.Current.DisplayName, null, null, null, null)
            : new SchoolHeader(p.Name, p.InstitutionalCode, p.Address, p.Phone, p.Email);
    }
}

/// <summary>Calendário letivo e turmas, para indicadores (contrato público do módulo School).</summary>
public sealed record TermInfo(int Number, string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record YearInfo(Guid Id, int Year, bool IsActive, IReadOnlyList<TermInfo> Terms);

public sealed record ClassroomInfo(Guid Id, string Name, string Shift, int GradeOrder);

public interface ISchoolCalendar
{
    Task<IReadOnlyList<YearInfo>> GetYearsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ClassroomInfo>> GetClassroomsAsync(Guid yearId, CancellationToken ct = default);
}

internal sealed class SchoolCalendar(SchoolDbContext db) : ISchoolCalendar
{
    public async Task<IReadOnlyList<YearInfo>> GetYearsAsync(CancellationToken ct = default)
    {
        var years = await db.Years.AsNoTracking().OrderByDescending(y => y.Year).ToListAsync(ct);
        var terms = await db.Terms.AsNoTracking().OrderBy(t => t.Number).ToListAsync(ct);
        return years.Select(y => new YearInfo(y.Id, y.Year, y.Status == SchoolYearStatus.Active,
            terms.Where(t => t.SchoolYearId == y.Id).Select(t => new TermInfo(t.Number, t.Name, t.StartsOn, t.EndsOn)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<ClassroomInfo>> GetClassroomsAsync(Guid yearId, CancellationToken ct = default) =>
        (await db.Classrooms.AsNoTracking().Include(c => c.Grade).Where(c => c.SchoolYearId == yearId).ToListAsync(ct))
            .OrderBy(c => c.Grade!.SortOrder).ThenBy(c => c.Section)
            .Select(c => new ClassroomInfo(c.Id, Classroom.DisplayName(c.Grade!.ShortName, c.Section, c.Shift), ShiftName(c.Shift), c.Grade.SortOrder))
            .ToList();

    private static string ShiftName(Shift s) => s switch
    {
        Shift.Manha => "Manhã",
        Shift.Tarde => "Tarde",
        Shift.Noite => "Noite",
        _ => "Integral",
    };
}
