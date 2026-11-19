using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.School;

namespace Sinedu.Tenant.Students;

public sealed class StudentsModule : IModule
{
    public string Name => "Students";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<StudentsDbContext>(StudentsDbContext.Schema);
        services.AddScoped<IStudentRegistry, StudentRegistry>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/students");

        api.MapGet("/", SearchAsync).RequireAuthorization(Policies.StudentReader);
        api.MapGet("/{id:guid}", DetailsAsync).RequireAuthorization(Policies.StudentReader);
        api.MapPost("/", CreateAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapPost("/{id:guid}/move", MoveAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapPut("/{id:guid}/guardians/{guardianId:guid}/contact", UpdateContactAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapPost("/{id:guid}/guardians", AddGuardianAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapPut("/{id:guid}/guardians/{guardianId:guid}", UpdateGuardianAsync).RequireAuthorization(Policies.PedagogicalStaff);
        api.MapDelete("/{id:guid}/guardians/{guardianId:guid}", UnlinkGuardianAsync).RequireAuthorization(Policies.PedagogicalStaff);

        // Contagem por turma: número agregado, sem dados pessoais (Admin técnico também vê a estrutura).
        api.MapGet("/classroom-counts", async (StudentsDbContext db, IViewerScopeProvider scopes, CancellationToken ct) =>
        {
            var scope = await scopes.GetAsync(ct);
            var query = db.Enrollments.AsNoTracking().Where(e => e.Status == EnrollmentStatus.Ativa);
            if (!scope.AllClassrooms) query = query.Where(e => scope.ClassroomIds.Contains(e.ClassroomId));
            return await query.GroupBy(e => e.ClassroomId).Select(g => new { ClassroomId = g.Key, Students = g.Count() }).ToListAsync(ct);
        });
    }

    private static async Task<IResult> SearchAsync(
        string? query, Guid? classroomId, int? page, int? pageSize,
        StudentsDbContext db, IViewerScopeProvider scopes, IClassroomDirectory classrooms, TimeProvider clock, CancellationToken ct)
    {
        var scope = await scopes.GetAsync(ct);
        var size = Math.Clamp(pageSize ?? 25, 5, 100);
        var current = Math.Max(page ?? 1, 1);

        // Base: matrícula ativa. Quem não vê todas as turmas só enxerga alunos das próprias turmas.
        var enrollments = db.Enrollments.AsNoTracking().Where(e => e.Status == EnrollmentStatus.Ativa);
        if (!scope.AllClassrooms) enrollments = enrollments.Where(e => scope.ClassroomIds.Contains(e.ClassroomId));
        if (classroomId is { } cid) enrollments = enrollments.Where(e => e.ClassroomId == cid);

        var students = db.Students.AsNoTracking().Where(s => enrollments.Any(e => e.StudentId == s.Id));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var search = Student.ToSearch(term);
            students = digits.Length >= 4 && digits.Length == term.Length
                ? students.Where(s => s.InstitutionalCode != null && s.InstitutionalCode.StartsWith(digits))
                : students.Where(s => EF.Functions.ILike(s.SearchName, $"%{search}%"));
        }

        var total = await students.CountAsync(ct);
        var rows = await students
            .OrderBy(s => s.SearchName)
            .Skip((current - 1) * size).Take(size)
            .Select(s => new
            {
                s.Id, s.FullName, s.SocialName, s.InstitutionalCode, s.BirthDate, s.Status,
                Enrollment = s.Enrollments.Where(e => e.Status == EnrollmentStatus.Ativa).Select(e => new { e.ClassroomId, e.CallNumber }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var names = await classrooms.GetAsync(rows.Where(r => r.Enrollment != null).Select(r => r.Enrollment!.ClassroomId), ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = size,
            items = rows.Select(r => new StudentRow(
                r.Id, r.SocialName ?? r.FullName, r.SocialName is null ? null : r.FullName, r.InstitutionalCode, Age(r.BirthDate, today),
                r.Enrollment is null ? null : new ClassroomLink(r.Enrollment.ClassroomId, names.GetValueOrDefault(r.Enrollment.ClassroomId)?.Name ?? "—"),
                r.Enrollment?.CallNumber, r.Status)),
        });
    }

    private static async Task<IResult> DetailsAsync(
        Guid id, StudentsDbContext db, IViewerScopeProvider scopes, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var scope = await scopes.GetAsync(ct);
        var student = await db.Students.AsNoTracking()
            .Include(s => s.Enrollments)
            .Include(s => s.Addresses.Where(a => a.ValidTo == null))
            .Include(s => s.Guardians).ThenInclude(g => g.Guardian)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        var visible = student is not null
            && (scope.AllClassrooms || student.Enrollments.Any(e => e.Status == EnrollmentStatus.Ativa && scope.ClassroomIds.Contains(e.ClassroomId)));
        if (!visible)
        {
            // Mesmo 404 para "não existe" e "fora do seu escopo": não revela existência.
            await audit.WriteAsync(new AuditEntry("student_access_denied", AuditResult.Denied, "student", id.ToString(), StudentId: id), ct);
            return Results.NotFound();
        }

        var names = await classrooms.GetAsync(student!.Enrollments.Select(e => e.ClassroomId), ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var active = student.Enrollments.FirstOrDefault(e => e.Status == EnrollmentStatus.Ativa);

        return Results.Ok(new StudentDetails(
            student.Id,
            student.FullName,
            student.SocialName,
            student.BirthDate,
            Age(student.BirthDate, today),
            student.InstitutionalCode,
            student.InstitutionalEmail,
            student.Status,
            active is null ? null : new ClassroomLink(active.ClassroomId, names.GetValueOrDefault(active.ClassroomId)?.Name ?? "—"),
            student.Enrollments.OrderByDescending(e => e.StartsOn).Select(e => new EnrollmentView(
                e.Id, e.ClassroomId, names.GetValueOrDefault(e.ClassroomId)?.Name ?? "—", names.GetValueOrDefault(e.ClassroomId)?.Year,
                e.Status, e.StartsOn, e.EndsOn, e.CallNumber)).ToList(),
            Guardians: scope.SeesGuardians
                ? student.Guardians.OrderByDescending(g => g.IsPrimaryContact).Select(g => new GuardianView(
                    g.GuardianId, g.Guardian!.FullName, g.Relationship, g.IsPrimaryContact, g.ContactAuthorized,
                    g.Guardian.Phone, g.Guardian.Email, scope.SeesAddress ? g.Guardian.Document : null,
                    scope.CanEditStudents ? g.ContactBasis : null)).ToList()
                : null,
            Address: scope.SeesAddress ? student.Addresses.FirstOrDefault()?.Line : null,
            CanEdit: scope.CanEditStudents));
    }

    private static async Task<IResult> CreateAsync(
        CreateStudentRequest body, StudentsDbContext db, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var errors = Validate(body.FullName, body.InstitutionalCode);
        var classroom = await classrooms.FindAsync(body.ClassroomId, ct);
        if (classroom is null) errors["classroomId"] = ["Turma inválida."];
        if (body.InstitutionalCode is { Length: > 0 } code && await db.Students.AnyAsync(s => s.InstitutionalCode == code.Trim(), ct))
        {
            errors["institutionalCode"] = ["Já existe um aluno com este código nesta escola."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var student = new Student
        {
            Id = Guid.CreateVersion7(),
            FullName = body.FullName.Trim(),
            SocialName = string.IsNullOrWhiteSpace(body.SocialName) ? null : body.SocialName.Trim(),
            SearchName = Student.ToSearch((body.SocialName ?? "") + " " + body.FullName),
            BirthDate = body.BirthDate,
            InstitutionalCode = string.IsNullOrWhiteSpace(body.InstitutionalCode) ? null : body.InstitutionalCode.Trim(),
            InstitutionalEmail = body.InstitutionalEmail?.Trim().ToLowerInvariant(),
            Status = StudentStatus.Active,
            Source = DataSource.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };
        student.Enrollments.Add(new Enrollment
        {
            Id = Guid.CreateVersion7(), ClassroomId = classroom!.Id, SchoolYearId = classroom.SchoolYearId, Status = EnrollmentStatus.Ativa,
            StartsOn = today, CallNumber = body.CallNumber,
        });
        if (!string.IsNullOrWhiteSpace(body.Address))
        {
            student.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), Line = body.Address.Trim(), ValidFrom = today, Source = DataSource.Manual });
        }

        foreach (var g in body.Guardians ?? [])
        {
            var guardian = new Guardian { Id = Guid.CreateVersion7(), FullName = g.FullName.Trim(), Document = g.Document?.Trim(), Phone = g.Phone?.Trim(), Email = g.Email?.Trim().ToLowerInvariant(), Source = DataSource.Manual };
            student.Guardians.Add(new StudentGuardian { Guardian = guardian, GuardianId = guardian.Id, Relationship = g.Relationship, IsPrimaryContact = g.IsPrimaryContact, ContactAuthorized = g.ContactAuthorized });
        }

        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("student_created", ResourceType: "student", ResourceId: student.Id.ToString(), StudentId: student.Id), ct);
        return Results.Created($"/api/students/{student.Id}", new { student.Id });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateStudentRequest body, StudentsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var student = await db.Students.Include(s => s.Addresses.Where(a => a.ValidTo == null)).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return Results.NotFound();

        var errors = Validate(body.FullName, body.InstitutionalCode);
        if (body.InstitutionalCode is { Length: > 0 } code && await db.Students.AnyAsync(s => s.Id != id && s.InstitutionalCode == code.Trim(), ct))
        {
            errors["institutionalCode"] = ["Já existe um aluno com este código nesta escola."];
        }

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow();
        student.FullName = body.FullName.Trim();
        student.SocialName = string.IsNullOrWhiteSpace(body.SocialName) ? null : body.SocialName.Trim();
        student.SearchName = Student.ToSearch((student.SocialName ?? "") + " " + student.FullName);
        student.BirthDate = body.BirthDate;
        student.InstitutionalCode = string.IsNullOrWhiteSpace(body.InstitutionalCode) ? null : body.InstitutionalCode.Trim();
        student.InstitutionalEmail = body.InstitutionalEmail?.Trim().ToLowerInvariant();
        student.UpdatedAt = now;

        // Endereço versionado: o anterior é encerrado, não sobrescrito.
        var currentAddress = student.Addresses.FirstOrDefault();
        var newAddress = body.Address?.Trim();
        if (!string.IsNullOrEmpty(newAddress) && currentAddress?.Line != newAddress)
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (currentAddress is not null) currentAddress.ValidTo = today;
            db.Addresses.Add(new StudentAddress { Id = Guid.CreateVersion7(), StudentId = id, Line = newAddress, ValidFrom = today, Source = DataSource.Manual });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: 409, title: "O cadastro foi alterado por outra pessoa. Recarregue e tente de novo.");
        }

        await audit.WriteAsync(new AuditEntry("student_updated", ResourceType: "student", ResourceId: id.ToString(), StudentId: id), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Contato do responsável e autorização de comunicação (spec 6.1). Autorizar exige fundamento e guarda quem e quando.
    /// Nada é presumido: sem autorização, o responsável não recebe comunicações.
    /// </summary>
    private static async Task<IResult> UpdateContactAsync(
        Guid id, Guid guardianId, UpdateContactRequest body, ClaimsPrincipal user, StudentsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var link = await db.StudentGuardians.Include(g => g.Guardian).FirstOrDefaultAsync(g => g.StudentId == id && g.GuardianId == guardianId, ct);
        if (link is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        var email = string.IsNullOrWhiteSpace(body.Email) ? null : body.Email.Trim().ToLowerInvariant();
        if (email is not null && (email.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(email, out _)))
        {
            errors["email"] = ["Informe um e-mail válido."];
        }

        var basis = string.IsNullOrWhiteSpace(body.ContactBasis) ? null : body.ContactBasis.Trim();
        if (body.ContactAuthorized && (basis is null || basis.Length < 5))
        {
            errors["contactBasis"] = ["Informe o fundamento da autorização (ex.: termo assinado na matrícula)."];
        }

        if (basis?.Length > 300) errors["contactBasis"] = ["Use no máximo 300 caracteres."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        link.Guardian!.Email = email;
        link.Guardian.Phone = string.IsNullOrWhiteSpace(body.Phone) ? link.Guardian.Phone : body.Phone.Trim();
        if (body.IsPrimaryContact && !link.IsPrimaryContact)
        {
            await db.StudentGuardians.Where(g => g.StudentId == id && g.GuardianId != guardianId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsPrimaryContact, false), ct);
        }

        link.IsPrimaryContact = body.IsPrimaryContact;
        if (body.ContactAuthorized != link.ContactAuthorized || (body.ContactAuthorized && basis != link.ContactBasis))
        {
            link.ContactAuthorized = body.ContactAuthorized;
            link.ContactBasis = body.ContactAuthorized ? basis : null;
            link.ContactAuthorizedAt = body.ContactAuthorized ? clock.GetUtcNow() : null;
            link.ContactAuthorizedBy = body.ContactAuthorized ? Guid.Parse(user.FindFirstValue("sub")!) : null;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry(body.ContactAuthorized ? "guardian_contact_authorized" : "guardian_contact_updated",
            ResourceType: "guardian", ResourceId: guardianId.ToString(), StudentId: id), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Novo responsável cadastrado na escola (F2-06). Sempre uma pessoa nova: nunca mescla com outro cadastro só pelo nome.
    /// Autorizar contato exige o fundamento, como na edição de contato.
    /// </summary>
    private static async Task<IResult> AddGuardianAsync(
        Guid id, AddGuardianRequest body, ClaimsPrincipal user, StudentsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Students.AnyAsync(s => s.Id == id, ct)) return Results.NotFound();

        var errors = ValidateGuardian(body.FullName, body.Relationship);
        var email = string.IsNullOrWhiteSpace(body.Email) ? null : body.Email.Trim().ToLowerInvariant();
        if (email is not null && (email.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(email, out _))) errors["email"] = ["Informe um e-mail válido."];
        var phone = string.IsNullOrWhiteSpace(body.Phone) ? null : body.Phone.Trim();
        if (phone?.Length > 30) errors["phone"] = ["Use no máximo 30 caracteres."];
        var basis = string.IsNullOrWhiteSpace(body.ContactBasis) ? null : body.ContactBasis.Trim();
        if (body.ContactAuthorized && (basis is null || basis.Length < 5)) errors["contactBasis"] = ["Informe o fundamento da autorização (ex.: termo assinado na matrícula)."];
        if (basis?.Length > 300) errors["contactBasis"] = ["Use no máximo 300 caracteres."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var guardian = new Guardian { Id = Guid.CreateVersion7(), FullName = body.FullName.Trim(), Phone = phone, Email = email, Source = DataSource.Manual };
        var now = clock.GetUtcNow();
        if (body.IsPrimaryContact)
        {
            await db.StudentGuardians.Where(g => g.StudentId == id).ExecuteUpdateAsync(s => s.SetProperty(g => g.IsPrimaryContact, false), ct);
        }

        db.Guardians.Add(guardian);
        db.StudentGuardians.Add(new StudentGuardian
        {
            StudentId = id,
            GuardianId = guardian.Id,
            Relationship = body.Relationship!.Value,
            IsPrimaryContact = body.IsPrimaryContact,
            ContactAuthorized = body.ContactAuthorized,
            ContactBasis = body.ContactAuthorized ? basis : null,
            ContactAuthorizedAt = body.ContactAuthorized ? now : null,
            ContactAuthorizedBy = body.ContactAuthorized ? Guid.Parse(user.FindFirstValue("sub")!) : null,
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("guardian_added", ResourceType: "guardian", ResourceId: guardian.Id.ToString(), StudentId: id), ct);
        return Results.Created($"/api/students/{id}/guardians/{guardian.Id}", new { guardian.Id });
    }

    /// <summary>Corrige nome e parentesco. O nome vale para todos os alunos ligados à mesma pessoa (irmãos).</summary>
    private static async Task<IResult> UpdateGuardianAsync(
        Guid id, Guid guardianId, UpdateGuardianRequest body, StudentsDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var link = await db.StudentGuardians.Include(g => g.Guardian).FirstOrDefaultAsync(g => g.StudentId == id && g.GuardianId == guardianId, ct);
        if (link is null) return Results.NotFound();

        var errors = ValidateGuardian(body.FullName, body.Relationship);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        link.Guardian!.FullName = body.FullName.Trim();
        link.Relationship = body.Relationship!.Value;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("guardian_updated", ResourceType: "guardian", ResourceId: guardianId.ToString(), StudentId: id), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Desfaz o vínculo com este aluno. O cadastro da pessoa continua (pode ser responsável por irmãos e aparece no
    /// histórico de comunicações já enviadas).
    /// </summary>
    private static async Task<IResult> UnlinkGuardianAsync(Guid id, Guid guardianId, StudentsDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var removed = await db.StudentGuardians.Where(g => g.StudentId == id && g.GuardianId == guardianId).ExecuteDeleteAsync(ct);
        if (removed == 0) return Results.NotFound();
        await audit.WriteAsync(new AuditEntry("guardian_unlinked", ResourceType: "guardian", ResourceId: guardianId.ToString(), StudentId: id), ct);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateGuardian(string? fullName, GuardianRelationship? relationship)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3) errors["fullName"] = ["Informe o nome completo."];
        else if (fullName.Trim().Length > 200) errors["fullName"] = ["Use no máximo 200 caracteres."];
        if (relationship is null || !Enum.IsDefined(relationship.Value)) errors["relationship"] = ["Escolha o parentesco."];
        return errors;
    }

    /// <summary>Remanejamento/transferência interna: encerra a matrícula ativa e abre outra (histórico preservado).</summary>
    private static async Task<IResult> MoveAsync(
        Guid id, MoveRequest body, StudentsDbContext db, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var active = await db.Enrollments.FirstOrDefaultAsync(e => e.StudentId == id && e.Status == EnrollmentStatus.Ativa, ct);
        if (active is null) return Results.NotFound();
        var target = await classrooms.FindAsync(body.ClassroomId, ct);
        if (target is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["classroomId"] = ["Turma inválida."] });
        if (target.Id == active.ClassroomId) return Results.ValidationProblem(new Dictionary<string, string[]> { ["classroomId"] = ["O aluno já está nesta turma."] });

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        active.Status = EnrollmentStatus.Remanejada;
        active.EndsOn = today;
        await db.SaveChangesAsync(ct);
        db.Enrollments.Add(new Enrollment
        {
            Id = Guid.CreateVersion7(), StudentId = id, ClassroomId = target.Id, SchoolYearId = target.SchoolYearId, Status = EnrollmentStatus.Ativa,
            StartsOn = today, CallNumber = body.CallNumber,
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("student_moved", ResourceType: "student", ResourceId: id.ToString(), StudentId: id), ct);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> Validate(string fullName, string? code)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3) errors["fullName"] = ["Informe o nome completo."];
        if (code is { Length: > 0 } && !code.Trim().All(char.IsDigit)) errors["institutionalCode"] = ["O código deve ter só números."];
        return errors;
    }

    private static int? Age(DateOnly? birth, DateOnly today)
    {
        if (birth is not { } b) return null;
        var age = today.Year - b.Year;
        return b > today.AddYears(-age) ? age - 1 : age;
    }
}

public sealed record ClassroomLink(Guid Id, string Name);

public sealed record StudentRow(Guid Id, string Name, string? CivilName, string? InstitutionalCode, int? Age, ClassroomLink? Classroom, int? CallNumber, StudentStatus Status);

public sealed record EnrollmentView(Guid Id, Guid ClassroomId, string Classroom, int? Year, EnrollmentStatus Status, DateOnly StartsOn, DateOnly? EndsOn, int? CallNumber);

public sealed record GuardianView(
    Guid Id, string Name, GuardianRelationship Relationship, bool IsPrimaryContact, bool ContactAuthorized, string? Phone, string? Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Document,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContactBasis = null);

/// <summary>
/// Painel do aluno. Responsáveis e endereço são OMITIDOS (não nulos) para quem não pode vê-los.
/// Nunca contém dados de Saúde ou Proteção — esses têm endpoints próprios no módulo ProtectedRecords.
/// </summary>
public sealed record StudentDetails(
    Guid Id,
    string FullName,
    string? SocialName,
    DateOnly? BirthDate,
    int? Age,
    string? InstitutionalCode,
    string? InstitutionalEmail,
    StudentStatus Status,
    ClassroomLink? Classroom,
    IReadOnlyList<EnrollmentView> Enrollments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GuardianView>? Guardians,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Address,
    bool CanEdit);

public sealed record GuardianInput(string FullName, GuardianRelationship Relationship, string? Document, string? Phone, string? Email, bool IsPrimaryContact, bool ContactAuthorized);

public sealed record CreateStudentRequest(
    string FullName, string? SocialName, DateOnly? BirthDate, string? InstitutionalCode, string? InstitutionalEmail, Guid ClassroomId, int? CallNumber,
    string? Address, IReadOnlyList<GuardianInput>? Guardians);

public sealed record UpdateStudentRequest(
    string FullName, string? SocialName, DateOnly? BirthDate, string? InstitutionalCode, string? InstitutionalEmail, string? Address);

public sealed record MoveRequest(Guid ClassroomId, int? CallNumber);

public sealed record AddGuardianRequest(
    string FullName, GuardianRelationship? Relationship, string? Phone, string? Email, bool IsPrimaryContact, bool ContactAuthorized, string? ContactBasis);

public sealed record UpdateGuardianRequest(string FullName, GuardianRelationship? Relationship);

public sealed record UpdateContactRequest(string? Email, string? Phone, bool IsPrimaryContact, bool ContactAuthorized, string? ContactBasis);
