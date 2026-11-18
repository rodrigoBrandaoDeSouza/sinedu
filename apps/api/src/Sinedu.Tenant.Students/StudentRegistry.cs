using Microsoft.EntityFrameworkCore;

namespace Sinedu.Tenant.Students;

public sealed record GuardianSnapshot(Guid Id, string Name, string? Document, string? Phone, GuardianRelationship Relationship);

/// <summary>Estado atual de um aluno, usado para comparar com uma linha importada.</summary>
public sealed record StudentSnapshot(
    Guid Id, string FullName, string SearchName, DateOnly? BirthDate, string? InstitutionalCode, string? InstitutionalEmail,
    Guid? ActiveClassroomId, int? CallNumber, string? Address, IReadOnlyList<GuardianSnapshot> Guardians);

public sealed record GuardianImport(string Name, string? Document, string? Phone, GuardianRelationship Relationship);

/// <summary>Dados de uma linha já validada. Campos nulos significam "não informado" — nunca apagam o que existe.</summary>
public sealed record StudentImport(
    string FullName, string? InstitutionalCode, DateOnly? BirthDate, string? InstitutionalEmail,
    Guid ClassroomId, Guid SchoolYearId, DateOnly EnrollmentStart, int? CallNumber, string? Address, GuardianImport? Guardian);

public enum StudentApplyOutcome
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>Contrato público do módulo Students para a importação (sem expor tabelas).</summary>
public interface IStudentRegistry
{
    Task<IReadOnlyList<StudentSnapshot>> FindByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default);

    /// <summary>Candidatos por nome normalizado idêntico — só sugestão, nunca união automática.</summary>
    Task<IReadOnlyList<StudentSnapshot>> FindByNamesAsync(IReadOnlyCollection<string> searchNames, CancellationToken ct = default);

    Task<StudentApplyOutcome> ApplyAsync(StudentImport data, Guid? existingId, CancellationToken ct = default);

    Task<IReadOnlyList<StudentSnapshot>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

internal sealed class StudentRegistry(StudentsDbContext db, TimeProvider clock) : IStudentRegistry
{
    public async Task<IReadOnlyList<StudentSnapshot>> FindByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default) =>
        await Load(db.Students.Where(s => s.InstitutionalCode != null && codes.Contains(s.InstitutionalCode)), ct);

    public async Task<IReadOnlyList<StudentSnapshot>> FindByNamesAsync(IReadOnlyCollection<string> searchNames, CancellationToken ct = default) =>
        await Load(db.Students.Where(s => searchNames.Contains(s.SearchName)), ct);

    public async Task<IReadOnlyList<StudentSnapshot>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await Load(db.Students.Where(s => ids.Contains(s.Id)), ct);

    public async Task<StudentApplyOutcome> ApplyAsync(StudentImport data, Guid? existingId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (existingId is null)
        {
            var student = new Student
            {
                Id = Guid.CreateVersion7(),
                FullName = data.FullName,
                SearchName = Student.ToSearch(data.FullName),
                BirthDate = data.BirthDate,
                InstitutionalCode = data.InstitutionalCode,
                InstitutionalEmail = data.InstitutionalEmail,
                Status = StudentStatus.Active,
                Source = DataSource.Import,
                CreatedAt = now,
                UpdatedAt = now,
            };
            student.Enrollments.Add(new Enrollment
            {
                Id = Guid.CreateVersion7(), ClassroomId = data.ClassroomId, SchoolYearId = data.SchoolYearId, Status = EnrollmentStatus.Ativa,
                StartsOn = data.EnrollmentStart, CallNumber = data.CallNumber,
            });
            if (data.Address is not null)
            {
                student.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), Line = data.Address, ValidFrom = today, Source = DataSource.Import });
            }

            if (data.Guardian is { } g)
            {
                var guardian = NewGuardian(g);
                student.Guardians.Add(new StudentGuardian { Guardian = guardian, GuardianId = guardian.Id, Relationship = g.Relationship, IsPrimaryContact = true });
            }

            db.Students.Add(student);
            await db.SaveChangesAsync(ct);
            return StudentApplyOutcome.Created;
        }

        var existing = await db.Students
            .Include(s => s.Enrollments.Where(e => e.Status == EnrollmentStatus.Ativa))
            .Include(s => s.Addresses.Where(a => a.ValidTo == null))
            .Include(s => s.Guardians).ThenInclude(x => x.Guardian)
            .FirstAsync(s => s.Id == existingId, ct);
        var changed = false;

        // Regra da spec: valor vazio na planilha nunca apaga o que está preenchido.
        changed |= Set(existing.FullName, data.FullName, v => { existing.FullName = v; existing.SearchName = Student.ToSearch((existing.SocialName ?? "") + " " + v); });
        changed |= Set(existing.InstitutionalCode, data.InstitutionalCode, v => existing.InstitutionalCode = v);
        changed |= Set(existing.InstitutionalEmail, data.InstitutionalEmail, v => existing.InstitutionalEmail = v);
        if (data.BirthDate is { } birth && existing.BirthDate != birth)
        {
            existing.BirthDate = birth;
            changed = true;
        }

        var active = existing.Enrollments.FirstOrDefault();
        if (active is null || active.ClassroomId != data.ClassroomId)
        {
            if (active is not null)
            {
                active.Status = EnrollmentStatus.Remanejada;
                active.EndsOn = today;
                await db.SaveChangesAsync(ct);
            }

            db.Enrollments.Add(new Enrollment
            {
                Id = Guid.CreateVersion7(), StudentId = existing.Id, ClassroomId = data.ClassroomId, SchoolYearId = data.SchoolYearId,
                Status = EnrollmentStatus.Ativa, StartsOn = active is null ? data.EnrollmentStart : today, CallNumber = data.CallNumber,
            });
            changed = true;
        }
        else if (data.CallNumber is { } call && active.CallNumber != call)
        {
            active.CallNumber = call;
            changed = true;
        }

        var address = existing.Addresses.FirstOrDefault();
        if (data.Address is not null && address?.Line != data.Address)
        {
            if (address is not null) address.ValidTo = today;
            db.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), StudentId = existing.Id, Line = data.Address, ValidFrom = today, Source = DataSource.Import });
            changed = true;
        }

        if (data.Guardian is { } guardianData)
        {
            var link = existing.Guardians.FirstOrDefault(x => SameGuardian(x.Guardian!, guardianData));
            if (link is null)
            {
                var guardian = NewGuardian(guardianData);
                db.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = existing.Id, Guardian = guardian, GuardianId = guardian.Id, Relationship = guardianData.Relationship,
                    IsPrimaryContact = existing.Guardians.Count == 0,
                });
                changed = true;
            }
            else
            {
                changed |= Set(link.Guardian!.Phone, guardianData.Phone, v => link.Guardian.Phone = v);
                changed |= Set(link.Guardian.Document, guardianData.Document, v => link.Guardian.Document = v);
            }
        }

        if (!changed)
        {
            return StudentApplyOutcome.Unchanged;
        }

        existing.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return StudentApplyOutcome.Updated;
    }

    public static bool SameGuardian(Guardian current, GuardianImport incoming) =>
        (incoming.Document is not null && current.Document == incoming.Document)
        || Student.ToSearch(current.FullName) == Student.ToSearch(incoming.Name);

    private static Guardian NewGuardian(GuardianImport g) =>
        new() { Id = Guid.CreateVersion7(), FullName = g.Name, Document = g.Document, Phone = g.Phone, Source = DataSource.Import };

    private static bool Set(string? current, string? incoming, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(incoming) || string.Equals(current, incoming, StringComparison.Ordinal))
        {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static async Task<IReadOnlyList<StudentSnapshot>> Load(IQueryable<Student> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Include(s => s.Enrollments.Where(e => e.Status == EnrollmentStatus.Ativa))
            .Include(s => s.Addresses.Where(a => a.ValidTo == null))
            .Include(s => s.Guardians).ThenInclude(g => g.Guardian)
            .ToListAsync(ct);
        return rows.Select(s => new StudentSnapshot(
            s.Id, s.FullName, s.SearchName, s.BirthDate, s.InstitutionalCode, s.InstitutionalEmail,
            s.Enrollments.FirstOrDefault()?.ClassroomId, s.Enrollments.FirstOrDefault()?.CallNumber, s.Addresses.FirstOrDefault()?.Line,
            s.Guardians.Select(g => new GuardianSnapshot(g.GuardianId, g.Guardian!.FullName, g.Guardian.Document, g.Guardian.Phone, g.Relationship)).ToList()))
            .ToList();
    }
}
