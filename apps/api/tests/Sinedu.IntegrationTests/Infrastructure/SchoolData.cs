using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Persistence;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.IntegrationTests.Infrastructure;

/// <summary>Estrutura fictícia mínima numa escola de teste: ano ativo, 2 turmas e alunos em cada uma.</summary>
public sealed record SchoolSample(Guid YearId, Guid ClassroomX, Guid ClassroomY, Guid StudentInX, Guid StudentInY, string StudentInXName);

public static class SchoolData
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly Dictionary<string, SchoolSample> Samples = [];

    public static async Task<SchoolSample> EnsureAsync(SineduFixture fx, string slug)
    {
        await Lock.WaitAsync();
        try
        {
            if (Samples.TryGetValue(slug, out var existing)) return existing;

            var connection = await fx.SchoolConnectionAsync(slug, owner: true);
            await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(connection, SchoolDbContext.Schema).Options);
            await using var students = new StudentsDbContext(new DbContextOptionsBuilder<StudentsDbContext>().UseSineduPostgres(connection, StudentsDbContext.Schema).Options);

            var year = new SchoolYear { Id = Guid.CreateVersion7(), Year = 2026, StartsOn = new(2026, 2, 2), EndsOn = new(2026, 12, 18), Status = SchoolYearStatus.Active };
            var grade = new Grade { Id = Guid.CreateVersion7(), Name = "6º ano", ShortName = "6º", Stage = Stage.Fundamental, SortOrder = 6 };
            var x = new Classroom { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, GradeId = grade.Id, Section = "A", Shift = Shift.Manha, Status = ClassroomStatus.Active };
            var y = new Classroom { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, GradeId = grade.Id, Section = "B", Shift = Shift.Tarde, Status = ClassroomStatus.Active };
            school.AddRange(year, grade, x, y);
            await school.SaveChangesAsync();

            var inX = NewStudent($"ÁLVARO FICTÍCIO {slug.ToUpperInvariant()}", x, year);
            var inY = NewStudent($"BEATRIZ FICTÍCIA {slug.ToUpperInvariant()}", y, year);
            students.AddRange(inX, inY);
            await students.SaveChangesAsync();

            var sample = new SchoolSample(year.Id, x.Id, y.Id, inX.Id, inY.Id, inX.FullName);
            Samples[slug] = sample;
            return sample;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Liga o usuário de uma sessão de professor a uma turma (profissional + vínculo).</summary>
    public static async Task AssignTeacherAsync(SineduFixture fx, string slug, Guid userId, Guid classroomId)
    {
        var connection = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(connection, SchoolDbContext.Schema).Options);
        var staff = new StaffMember { Id = Guid.CreateVersion7(), FullName = "Professor de teste", UserId = userId, Function = StaffFunction.Professor, CreatedAt = DateTimeOffset.UtcNow };
        school.Staff.Add(staff);
        school.TeacherAssignments.Add(new TeacherAssignment { Id = Guid.CreateVersion7(), StaffMemberId = staff.Id, ClassroomId = classroomId, StartsOn = new(2026, 2, 2) });
        await school.SaveChangesAsync();
    }

    public static async Task GrantReadOnlyAsync(SineduFixture fx, string slug, Guid userId, Guid classroomId)
    {
        var connection = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var access = new AccessDbContext(new DbContextOptionsBuilder<AccessDbContext>().UseSineduPostgres(connection, AccessDbContext.Schema).Options);
        access.ClassroomGrants.Add(new ClassroomAccessGrant { Id = Guid.CreateVersion7(), UserId = userId, ClassroomId = classroomId, GrantedBy = userId, GrantedAt = DateTimeOffset.UtcNow });
        await access.SaveChangesAsync();
    }

    /// <summary>Aluno novo na turma X (testes que precisam de um aluno "limpo"). Devolve id e CGM.</summary>
    public static async Task<(Guid Id, string Code)> NewStudentAsync(SineduFixture fx, string slug)
    {
        var sample = await EnsureAsync(fx, slug);
        var connection = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(connection, SchoolDbContext.Schema).Options);
        await using var students = new StudentsDbContext(new DbContextOptionsBuilder<StudentsDbContext>().UseSineduPostgres(connection, StudentsDbContext.Schema).Options);
        var year = await school.Years.FirstAsync(y => y.Id == sample.YearId);
        var classroom = await school.Classrooms.FirstAsync(c => c.Id == sample.ClassroomX);
        var student = NewStudent($"CAIO FICTÍCIO {Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", classroom, year);
        students.Add(student);
        await students.SaveChangesAsync();
        return (student.Id, student.InstitutionalCode!);
    }

/// <summary>Turma nova no ano do sample (com os bimestres padrão garantidos) — para indicadores com contagem exata.</summary>
    public static async Task<Guid> NewClassroomAsync(SineduFixture fx, string slug, string section)
    {
        var sample = await EnsureAsync(fx, slug);
        var connection = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(connection, SchoolDbContext.Schema).Options);
        await Lock.WaitAsync();
        try
        {
            var year = await school.Years.Include(y => y.Terms).FirstAsync(y => y.Id == sample.YearId);
            if (year.Terms.Count == 0)
            {
                school.Terms.AddRange(SchoolModule.StandardTerms(year));
                await school.SaveChangesAsync();
            }
        }
        finally
        {
            Lock.Release();
        }

        var grade = await school.Classrooms.Where(c => c.Id == sample.ClassroomX).Select(c => c.GradeId).FirstAsync();
        var classroom = new Classroom { Id = Guid.CreateVersion7(), SchoolYearId = sample.YearId, GradeId = grade, Section = section, Shift = Shift.Noite, Status = ClassroomStatus.Active };
        school.Classrooms.Add(classroom);
        await school.SaveChangesAsync();
        return classroom.Id;
    }

    public static async Task<Guid> NewStudentInAsync(SineduFixture fx, string slug, Guid classroomId, string name)
    {
        var connection = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(connection, SchoolDbContext.Schema).Options);
        await using var students = new StudentsDbContext(new DbContextOptionsBuilder<StudentsDbContext>().UseSineduPostgres(connection, StudentsDbContext.Schema).Options);
        var classroom = await school.Classrooms.FirstAsync(c => c.Id == classroomId);
        var year = await school.Years.FirstAsync(y => y.Id == classroom.SchoolYearId);
        var student = NewStudent(name, classroom, year);
        students.Add(student);
        await students.SaveChangesAsync();
        return student.Id;
    }

    private static Student NewStudent(string name, Classroom classroom, SchoolYear year)
    {
        var student = new Student
        {
            Id = Guid.CreateVersion7(), FullName = name, SearchName = Student.ToSearch(name), Status = StudentStatus.Active, Source = DataSource.Manual,
            InstitutionalCode = Random.Shared.NextInt64(1_000_000_000, 1_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        student.Enrollments.Add(new Enrollment { Id = Guid.CreateVersion7(), ClassroomId = classroom.Id, SchoolYearId = year.Id, Status = EnrollmentStatus.Ativa, StartsOn = year.StartsOn });
        student.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), Line = "RUA FICTÍCIA, 100", ValidFrom = year.StartsOn, Source = DataSource.Manual });
        var guardian = new Guardian { Id = Guid.CreateVersion7(), FullName = "RESPONSÁVEL FICTÍCIO", Document = "000.000.000-00", Phone = "(00) 90000-0000", Source = DataSource.Manual };
        student.Guardians.Add(new StudentGuardian { Guardian = guardian, GuardianId = guardian.Id, Relationship = GuardianRelationship.Mae, IsPrimaryContact = true });
        return student;
    }
}
