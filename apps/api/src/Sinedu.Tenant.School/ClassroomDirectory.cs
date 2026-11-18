using Microsoft.EntityFrameworkCore;

namespace Sinedu.Tenant.School;

public sealed record ClassroomRef(Guid Id, string Name, Guid SchoolYearId, int Year, bool ActiveYear);

/// <summary>Contrato público do módulo School para outros módulos (ex.: Students), sem expor tabelas.</summary>
public interface IClassroomDirectory
{
    Task<IReadOnlyDictionary<Guid, ClassroomRef>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    Task<ClassroomRef?> FindAsync(Guid id, CancellationToken ct = default);
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
}
