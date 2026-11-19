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

namespace Sinedu.Tenant.School;

public sealed class SchoolModule : IModule
{
    public string Name => "School";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<SchoolDbContext>(SchoolDbContext.Schema);
        services.AddScoped<IHttpContextAccessorWrapper, HttpContextAccessorWrapper>();
        services.AddScoped<IViewerScopeProvider, ViewerScopeProvider>();
        services.AddScoped<IClassroomDirectory, ClassroomDirectory>();
        services.AddScoped<ISchoolProfileReader, SchoolProfileReader>();
        services.AddScoped<ISchoolCalendar, SchoolCalendar>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/school");

        // ---- Perfil da escola ----
        SchoolLogo.Map(api);

        api.MapGet("/profile", async (SchoolDbContext db, TenantScope tenant, CancellationToken ct) =>
            await db.Profile.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SchoolProfile { Name = tenant.Current.DisplayName });

        api.MapPut("/profile", async (ProfileRequest body, SchoolDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name)) return Invalid("name", "Informe o nome da escola.");
            var profile = await db.Profile.FirstOrDefaultAsync(ct);
            if (profile is null) db.Profile.Add(profile = new SchoolProfile { Name = body.Name });
            profile.Name = body.Name.Trim();
            profile.InstitutionalCode = body.InstitutionalCode?.Trim();
            profile.Address = body.Address?.Trim();
            profile.Phone = body.Phone?.Trim();
            profile.Email = body.Email?.Trim();
            profile.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SchoolConfig);

        // ---- Anos letivos e bimestres ----
        api.MapGet("/years", async (SchoolDbContext db, CancellationToken ct) =>
            await db.Years.AsNoTracking().Include(y => y.Terms).OrderByDescending(y => y.Year)
                .Select(y => new YearView(y.Id, y.Year, y.StartsOn, y.EndsOn, y.Status,
                    y.Terms.OrderBy(t => t.Number).Select(t => new TermView(t.Id, t.Number, t.Name, t.StartsOn, t.EndsOn)).ToList()))
                .ToListAsync(ct));

        api.MapPost("/years", async (YearRequest body, SchoolDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            if (body.Year is < 2000 or > 2100) return Invalid("year", "Ano inválido.");
            if (body.EndsOn <= body.StartsOn) return Invalid("endsOn", "O fim precisa ser depois do início.");
            if (await db.Years.AnyAsync(y => y.Year == body.Year, ct)) return Invalid("year", "Este ano letivo já existe.");

            var year = new SchoolYear { Id = Guid.CreateVersion7(), Year = body.Year, StartsOn = body.StartsOn, EndsOn = body.EndsOn, Status = body.Status };
            if (body.Status == SchoolYearStatus.Active)
            {
                await db.Years.Where(y => y.Status == SchoolYearStatus.Active).ExecuteUpdateAsync(s => s.SetProperty(y => y.Status, SchoolYearStatus.Closed), ct);
            }

            db.Years.Add(year);
            if (body.CreateStandardTerms)
            {
                year.Terms.AddRange(StandardTerms(year));
            }

            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("school_year_created", ResourceType: "school_year", ResourceId: year.Id.ToString()), ct);
            return Results.Created($"/api/school/years/{year.Id}", new { year.Id });
        }).RequireAuthorization(Policies.SchoolConfig);

        api.MapPut("/years/{id:guid}/terms", async (Guid id, TermRequest[] body, SchoolDbContext db, CancellationToken ct) =>
        {
            var year = await db.Years.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (year is null) return Results.NotFound();

            var ordered = body.OrderBy(t => t.StartsOn).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var t = ordered[i];
                if (t.EndsOn < t.StartsOn) return Invalid("terms", $"{t.Name}: o fim precisa ser depois do início.");
                if (t.StartsOn < year.StartsOn || t.EndsOn > year.EndsOn) return Invalid("terms", $"{t.Name} precisa estar dentro do ano letivo.");
                if (i > 0 && t.StartsOn <= ordered[i - 1].EndsOn) return Invalid("terms", $"{t.Name} se sobrepõe a {ordered[i - 1].Name}.");
            }

            db.Terms.RemoveRange(year.Terms);
            year.Terms = body.Select(t => new AcademicTerm { Id = Guid.CreateVersion7(), SchoolYearId = id, Number = t.Number, Name = t.Name.Trim(), StartsOn = t.StartsOn, EndsOn = t.EndsOn }).ToList();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SchoolConfig);

        api.MapPost("/years/{id:guid}/activate", async (Guid id, SchoolDbContext db, CancellationToken ct) =>
        {
            if (!await db.Years.AnyAsync(y => y.Id == id, ct)) return Results.NotFound();
            await db.Years.Where(y => y.Status == SchoolYearStatus.Active && y.Id != id).ExecuteUpdateAsync(s => s.SetProperty(y => y.Status, SchoolYearStatus.Closed), ct);
            await db.Years.Where(y => y.Id == id).ExecuteUpdateAsync(s => s.SetProperty(y => y.Status, SchoolYearStatus.Active), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SchoolConfig);

        // ---- Séries ----
        api.MapGet("/grades", async (SchoolDbContext db, CancellationToken ct) =>
            await db.Grades.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync(ct));

        api.MapPost("/grades", async (GradeRequest body, SchoolDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.ShortName)) return Invalid("name", "Informe o nome e a forma curta.");
            if (await db.Grades.AnyAsync(g => g.Stage == body.Stage && g.ShortName == body.ShortName.Trim(), ct)) return Invalid("shortName", "Série já cadastrada.");
            var grade = new Grade { Id = Guid.CreateVersion7(), Name = body.Name.Trim(), ShortName = body.ShortName.Trim(), Stage = body.Stage, SortOrder = body.SortOrder };
            db.Grades.Add(grade);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/school/grades/{grade.Id}", grade);
        }).RequireAuthorization(Policies.SchoolConfig);

        // ---- Turmas ----
        api.MapGet("/classrooms", async (Guid? yearId, SchoolDbContext db, IViewerScopeProvider scopes, CancellationToken ct) =>
        {
            var scope = await scopes.GetAsync(ct);
            var query = db.Classrooms.AsNoTracking().Where(c => yearId == null ? c.SchoolYear!.Status == SchoolYearStatus.Active : c.SchoolYearId == yearId);
            if (!scope.AllClassrooms)
            {
                query = query.Where(c => scope.ClassroomIds.Contains(c.Id));
            }

            return await query
                .OrderBy(c => c.Grade!.SortOrder).ThenBy(c => c.Section)
                .Select(c => new ClassroomView(
                    c.Id, Classroom.DisplayName(c.Grade!.ShortName, c.Section, c.Shift), c.GradeId, c.Grade.Name, c.Grade.Stage, c.Section, c.Shift,
                    c.Status, c.Capacity, c.ExternalCode, c.SchoolYearId,
                    c.Teachers.Where(t => t.EndsOn == null).Select(t => new TeacherView(t.Id, t.StaffMemberId, t.StaffMember!.FullName, t.Subject)).ToList()))
                .ToListAsync(ct);
        });

        api.MapGet("/classrooms/{id:guid}", async (Guid id, SchoolDbContext db, IViewerScopeProvider scopes, CancellationToken ct) =>
        {
            var scope = await scopes.GetAsync(ct);
            if (!scope.CanSeeClassroom(id)) return Results.NotFound();

            var c = await db.Classrooms.AsNoTracking().Include(x => x.Grade).Include(x => x.SchoolYear)
                .Include(x => x.Teachers.Where(t => t.EndsOn == null)).ThenInclude(t => t.StaffMember)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null
                ? Results.NotFound()
                : Results.Ok(new ClassroomView(
                    c.Id, Classroom.DisplayName(c.Grade!.ShortName, c.Section, c.Shift), c.GradeId, c.Grade.Name, c.Grade.Stage, c.Section, c.Shift,
                    c.Status, c.Capacity, c.ExternalCode, c.SchoolYearId,
                    c.Teachers.Select(t => new TeacherView(t.Id, t.StaffMemberId, t.StaffMember!.FullName, t.Subject)).ToList()));
        });

        api.MapPost("/classrooms", async (ClassroomRequest body, SchoolDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Section)) return Invalid("section", "Informe a turma (ex.: A).");
            if (!await db.Years.AnyAsync(y => y.Id == body.SchoolYearId, ct)) return Invalid("schoolYearId", "Ano letivo inválido.");
            if (!await db.Grades.AnyAsync(g => g.Id == body.GradeId, ct)) return Invalid("gradeId", "Série inválida.");
            var section = body.Section.Trim().ToUpperInvariant();
            if (await db.Classrooms.AnyAsync(c => c.SchoolYearId == body.SchoolYearId && c.GradeId == body.GradeId && c.Section == section && c.Shift == body.Shift, ct))
            {
                return Invalid("section", "Esta turma já existe.");
            }

            var classroom = new Classroom
            {
                Id = Guid.CreateVersion7(), SchoolYearId = body.SchoolYearId, GradeId = body.GradeId, Section = section, Shift = body.Shift,
                Capacity = body.Capacity, ExternalCode = body.ExternalCode?.Trim(), Status = ClassroomStatus.Active,
            };
            db.Classrooms.Add(classroom);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/school/classrooms/{classroom.Id}", new { classroom.Id });
        }).RequireAuthorization(Policies.SchoolConfig);

        // ---- Turmas concedidas ao perfil Consulta (D10) ----
        api.MapGet("/readonly-grants/{userId:guid}", async (Guid userId, Access.AccessDbContext access, CancellationToken ct) =>
            await access.ClassroomGrants.AsNoTracking().Where(g => g.UserId == userId && g.RevokedAt == null).Select(g => g.ClassroomId).ToListAsync(ct))
            .RequireAuthorization(Policies.SchoolConfig);

        api.MapPut("/readonly-grants/{userId:guid}", async (
            Guid userId, Guid[] classroomIds, System.Security.Claims.ClaimsPrincipal user, Access.AccessDbContext access, SchoolDbContext db,
            IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var isReadOnly = await access.Roles.AnyAsync(r => r.UserId == userId && r.Role == Roles.ReadOnly && r.RevokedAt == null, ct);
            if (!isReadOnly) return Invalid("userId", "A pessoa precisa ter o papel Consulta.");
            var valid = await db.Classrooms.Where(c => classroomIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            if (valid.Count != classroomIds.Distinct().Count()) return Invalid("classroomIds", "Turma inválida.");

            var now = clock.GetUtcNow();
            var actor = Guid.Parse(user.FindFirst("sub")!.Value);
            var current = await access.ClassroomGrants.Where(g => g.UserId == userId && g.RevokedAt == null).ToListAsync(ct);
            foreach (var grant in current.Where(g => !valid.Contains(g.ClassroomId))) grant.RevokedAt = now;
            foreach (var classroomId in valid.Where(id => current.All(g => g.ClassroomId != id)))
            {
                access.ClassroomGrants.Add(new Access.ClassroomAccessGrant { Id = Guid.CreateVersion7(), UserId = userId, ClassroomId = classroomId, GrantedBy = actor, GrantedAt = now });
            }

            await access.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("readonly_grants_changed", ResourceType: "user", ResourceId: userId.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SchoolConfig);

        // ---- Equipe e vínculos professor ↔ turma (P7: Pedagogo também mantém vínculos) ----
        api.MapGet("/staff", async (SchoolDbContext db, CancellationToken ct) =>
            await db.Staff.AsNoTracking().Where(s => s.Active).OrderBy(s => s.FullName)
                .Select(s => new { s.Id, s.FullName, s.Function, s.ProfessionalEmail, s.ProfessionalPhone, HasAccount = s.UserId != null })
                .ToListAsync(ct))
            .RequireAuthorization(Policies.SchoolTeam);

        api.MapPost("/staff", async (StaffRequest body, SchoolDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.FullName)) return Invalid("fullName", "Informe o nome.");
            var staff = new StaffMember
            {
                Id = Guid.CreateVersion7(), FullName = body.FullName.Trim(), Function = body.Function, UserId = body.UserId,
                ProfessionalEmail = body.ProfessionalEmail?.Trim().ToLowerInvariant(), ProfessionalPhone = body.ProfessionalPhone?.Trim(), CreatedAt = clock.GetUtcNow(),
            };
            db.Staff.Add(staff);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/school/staff/{staff.Id}", new { staff.Id });
        }).RequireAuthorization(Policies.SchoolTeam);

        api.MapPost("/classrooms/{id:guid}/teachers", async (Guid id, AssignTeacherRequest body, SchoolDbContext db, TimeProvider clock, IAuditLog audit, CancellationToken ct) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == id, ct)) return Results.NotFound();
            if (!await db.Staff.AnyAsync(s => s.Id == body.StaffMemberId && s.Active, ct)) return Invalid("staffMemberId", "Profissional inválido.");

            var assignment = new TeacherAssignment
            {
                Id = Guid.CreateVersion7(), ClassroomId = id, StaffMemberId = body.StaffMemberId, Subject = body.Subject?.Trim(),
                StartsOn = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            };
            db.TeacherAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("teacher_assigned", ResourceType: "classroom", ResourceId: id.ToString()), ct);
            return Results.Created($"/api/school/classrooms/{id}/teachers/{assignment.Id}", new { assignment.Id });
        }).RequireAuthorization(Policies.SchoolTeam);

        api.MapDelete("/classrooms/{id:guid}/teachers/{assignmentId:guid}", async (Guid id, Guid assignmentId, SchoolDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var updated = await db.TeacherAssignments.Where(t => t.Id == assignmentId && t.ClassroomId == id && t.EndsOn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.EndsOn, today), ct);
            return updated == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization(Policies.SchoolTeam);
    }

    /// <summary>4 bimestres de duração aproximadamente igual dentro do ano letivo (ajustáveis depois).</summary>
    public static IEnumerable<AcademicTerm> StandardTerms(SchoolYear year)
    {
        var totalDays = year.EndsOn.DayNumber - year.StartsOn.DayNumber + 1;
        for (var i = 0; i < 4; i++)
        {
            var start = year.StartsOn.AddDays(totalDays * i / 4);
            var end = i == 3 ? year.EndsOn : year.StartsOn.AddDays((totalDays * (i + 1) / 4) - 1);
            yield return new AcademicTerm { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, Number = i + 1, Name = $"{i + 1}º bimestre", StartsOn = start, EndsOn = end };
        }
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed record ProfileRequest(string Name, string? InstitutionalCode, string? Address, string? Phone, string? Email);

public sealed record YearRequest(int Year, DateOnly StartsOn, DateOnly EndsOn, SchoolYearStatus Status, bool CreateStandardTerms = true);

public sealed record TermRequest(int Number, string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record GradeRequest(string Name, string ShortName, Stage Stage, int SortOrder);

public sealed record ClassroomRequest(Guid SchoolYearId, Guid GradeId, string Section, Shift Shift, int? Capacity, string? ExternalCode);

public sealed record StaffRequest(string FullName, StaffFunction Function, Guid? UserId, string? ProfessionalEmail, string? ProfessionalPhone);

public sealed record AssignTeacherRequest(Guid StaffMemberId, string? Subject);

public sealed record YearView(Guid Id, int Year, DateOnly StartsOn, DateOnly EndsOn, SchoolYearStatus Status, IReadOnlyList<TermView> Terms);

public sealed record TermView(Guid Id, int Number, string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record TeacherView(Guid AssignmentId, Guid StaffMemberId, string Name, string? Subject);

public sealed record ClassroomView(
    Guid Id, string Name, Guid GradeId, string GradeName, Stage Stage, string Section, Shift Shift, ClassroomStatus Status, int? Capacity,
    string? ExternalCode, Guid SchoolYearId, IReadOnlyList<TeacherView> Teachers);
