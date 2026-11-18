using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Host.Dev;

/// <summary>
/// Carrega a planilha ANONIMIZADA de documentos de alunos (.doc/amostras) numa escola de desenvolvimento.
/// Idempotente: alunos já existentes (mesmo CGM) são mantidos. Não é a importação da Fase 3 —
/// não tem mapeamento assistido, prévia nem lote auditado; serve para popular escolas de teste.
/// Uso: dotnet run --project src/Sinedu.Tenant.Host -- import-sample escola-teste ../../.doc/amostras/alunos-2026-anonimizado.xlsx
/// </summary>
internal static partial class SampleImporter
{
    public static async Task RunAsync(IServiceProvider root, string slug, string path)
    {
        if (!root.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            throw new InvalidOperationException("import-sample só pode rodar em Development.");
        }

        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        if (resolution.Status != TenantResolutionStatus.Ready)
        {
            throw new InvalidOperationException($"Escola '{slug}' indisponível: {resolution.Status}.");
        }

        sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
        var school = sp.GetRequiredService<SchoolDbContext>();
        var students = sp.GetRequiredService<StudentsDbContext>();

        var year = await EnsureYearAsync(school, 2026);
        var grades = await school.Grades.ToListAsync();
        var classrooms = await school.Classrooms.Where(c => c.SchoolYearId == year.Id).ToListAsync();
        var existingCodes = (await students.Students.Where(s => s.InstitutionalCode != null).Select(s => s.InstitutionalCode!).ToListAsync()).ToHashSet();

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Original");
        int created = 0, skipped = 0, invalid = 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var code = Text(row.Cell(2));
            var name = Text(row.Cell(3));
            var classroomLabel = Text(row.Cell(4));
            if (code is null || name is null || classroomLabel is null || TurmaRegex().Match(classroomLabel) is not { Success: true } match)
            {
                invalid++;
                continue;
            }

            if (!existingCodes.Add(code))
            {
                skipped++;
                continue;
            }

            var classroom = await EnsureClassroomAsync(school, year, grades, classrooms, match);
            var student = new Student
            {
                Id = Guid.CreateVersion7(),
                FullName = name,
                SearchName = Student.ToSearch(name),
                BirthDate = row.Cell(5).TryGetValue<DateTime>(out var birth) ? DateOnly.FromDateTime(birth) : null,
                InstitutionalCode = code,
                InstitutionalEmail = Text(row.Cell(6))?.ToLowerInvariant(),
                Status = StudentStatus.Active,
                Source = DataSource.Import,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            student.Enrollments.Add(new Enrollment
            {
                Id = Guid.CreateVersion7(), ClassroomId = classroom.Id, SchoolYearId = year.Id, Status = EnrollmentStatus.Ativa, StartsOn = year.StartsOn,
                CallNumber = row.Cell(1).TryGetValue<int>(out var call) ? call : null,
            });
            if (Text(row.Cell(7)) is { } address)
            {
                student.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), Line = address, ValidFrom = today, Source = DataSource.Import });
            }

            if (Text(row.Cell(8)) is { } guardianName)
            {
                var guardian = new Guardian
                {
                    Id = Guid.CreateVersion7(), FullName = guardianName, Document = Text(row.Cell(9)), Phone = Text(row.Cell(25)), Source = DataSource.Import,
                };
                student.Guardians.Add(new StudentGuardian
                {
                    Guardian = guardian,
                    GuardianId = guardian.Id,
                    Relationship = Relationship(Text(row.Cell(11))),
                    IsPrimaryContact = true,
                    // A planilha não informa autorização de contato: não presumir (spec §6.1).
                    ContactAuthorized = false,
                });
            }

            students.Students.Add(student);
            created++;
            if (created % 200 == 0)
            {
                await students.SaveChangesAsync();
            }
        }

        await students.SaveChangesAsync();
        Console.WriteLine($"Escola {slug}: {created} alunos criados, {skipped} já existiam, {invalid} linhas ignoradas; {classrooms.Count} turmas no ano {year.Year}.");
    }

    private static async Task<SchoolYear> EnsureYearAsync(SchoolDbContext db, int value)
    {
        var year = await db.Years.FirstOrDefaultAsync(y => y.Year == value);
        if (year is not null)
        {
            return year;
        }

        year = new SchoolYear
        {
            Id = Guid.CreateVersion7(), Year = value, StartsOn = new DateOnly(value, 2, 2), EndsOn = new DateOnly(value, 12, 18), Status = SchoolYearStatus.Active,
        };
        year.Terms.AddRange(SchoolModule.StandardTerms(year));
        db.Years.Add(year);
        await db.SaveChangesAsync();
        return year;
    }

    private static async Task<Classroom> EnsureClassroomAsync(SchoolDbContext db, SchoolYear year, List<Grade> grades, List<Classroom> classrooms, Match match)
    {
        var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var medio = match.Groups["o"].Value == "ª";
        var shortName = $"{number}{(medio ? "ª" : "º")}";
        var stage = medio ? Stage.Medio : Stage.Fundamental;

        var grade = grades.FirstOrDefault(g => g.Stage == stage && g.ShortName == shortName);
        if (grade is null)
        {
            grade = new Grade
            {
                Id = Guid.CreateVersion7(), ShortName = shortName, Stage = stage, Name = medio ? $"{number}ª série do Ensino Médio" : $"{number}º ano",
                SortOrder = medio ? 100 + number : number,
            };
            db.Grades.Add(grade);
            grades.Add(grade);
        }

        var section = match.Groups["t"].Value;
        var shift = match.Groups["s"].Value switch { "Manhã" => Shift.Manha, "Tarde" => Shift.Tarde, _ => Shift.Noite };
        var classroom = classrooms.FirstOrDefault(c => c.GradeId == grade.Id && c.Section == section && c.Shift == shift);
        if (classroom is null)
        {
            classroom = new Classroom { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, GradeId = grade.Id, Section = section, Shift = shift, Status = ClassroomStatus.Active };
            db.Classrooms.Add(classroom);
            classrooms.Add(classroom);
            await db.SaveChangesAsync();
        }

        return classroom;
    }

    private static GuardianRelationship Relationship(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "MÃE" => GuardianRelationship.Mae,
        "PAI" => GuardianRelationship.Pai,
        "AVÓS" or "AVÓ" or "AVÔ" => GuardianRelationship.Avos,
        "PRÓPRIO ALUNO" => GuardianRelationship.ProprioAluno,
        _ => GuardianRelationship.Outro,
    };

    private static string? Text(IXLCell cell)
    {
        var value = cell.GetFormattedString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>"6º A Manhã", "1ª D Noite".</summary>
    [GeneratedRegex(@"^(?<n>\d{1,2})(?<o>[ºª])\s+(?<t>[A-Z])\s+(?<s>Manhã|Tarde|Noite)$", RegexOptions.CultureInvariant)]
    private static partial Regex TurmaRegex();
}
