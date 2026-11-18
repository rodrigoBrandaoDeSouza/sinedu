using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Occurrences;

public sealed record SaveOccurrenceRequest(
    Guid StudentId, DateTimeOffset OccurredAt, Guid CategoryId, Guid? SeverityId, string? Location, string? Description,
    string? Involved, string? ActionsTaken, string? Referrals, string? Notes, bool Register = false, string? Reason = null, int ExpectedVersion = 0)
{
    public OccurrenceInput ToInput() => new(StudentId, OccurredAt, CategoryId, SeverityId, Location, Description, Involved, ActionsTaken, Referrals, Notes);
}

public sealed record StatusRequest(OccurrenceStatus Status, string? Reason, int ExpectedVersion);

public sealed record VersionRequest(int ExpectedVersion);

public sealed record CategoryRequest(string Name, bool IsActive = true);

/// <summary>
/// Ocorrências (Fase 5). Só Direção e Pedagogia (matriz §Ocorrências). Rascunho só para o autor.
/// Ocorrências de proteção/violência com informação sigilosa vão para o módulo protegido, não para cá.
/// </summary>
public sealed class OccurrencesModule : IModule
{
    public string Name => "Occurrences";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<OccurrencesDbContext>(OccurrencesDbContext.Schema);
        services.AddScoped<OccurrenceService>();
        services.AddScoped<OccurrenceDocuments>();
        services.AddScoped<IOccurrenceDirectory, OccurrenceDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/occurrences").RequireAuthorization(Policies.PedagogicalStaff).AddEndpointFilter(TranslateErrors);

        // ---- Categorias (configuráveis pela Direção) ----
        api.MapGet("/categories", async (OccurrencesDbContext db, CancellationToken ct) => new
        {
            categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct),
            severities = await db.Severities.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct),
        });

        api.MapPost("/categories", async (CategoryRequest body, OccurrencesDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var name = body.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Use de 2 a 80 caracteres."] });
            if (await db.Categories.AnyAsync(c => c.Name == name, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Já existe uma categoria com este nome."] });
            var category = new OccurrenceCategory { Id = Guid.CreateVersion7(), Name = name, IsActive = true, SortOrder = await db.Categories.CountAsync(ct) + 1 };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("occurrence_category_created", ResourceType: "occurrence_category", ResourceId: category.Id.ToString()), ct);
            return Results.Created($"/api/occurrences/categories/{category.Id}", category);
        }).RequireAuthorization(Policies.SchoolManager);

        api.MapPut("/categories/{id:guid}", async (Guid id, CategoryRequest body, OccurrencesDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null) return Results.NotFound();
            var name = body.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Use de 2 a 80 caracteres."] });
            category.Name = name;
            category.IsActive = body.IsActive;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("occurrence_category_updated", ResourceType: "occurrence_category", ResourceId: id.ToString()), ct);
            return Results.Ok(category);
        }).RequireAuthorization(Policies.SchoolManager);

        // ---- Ocorrências ----
        api.MapGet("/", async (Guid? studentId, Guid? classroomId, OccurrenceStatus? status, DateOnly? from, DateOnly? to, int? page,
            ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
        {
            var (items, total) = await s.ListAsync(new OccurrenceFilter(studentId, classroomId, status, from, to, page ?? 1), ActorOf(user).Id, ct);
            return Results.Ok(new { total, page = page ?? 1, pageSize = OccurrenceService.PageSize, items });
        });

        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.GetAsync(id, ActorOf(user).Id, ct) is { } o ? Results.Ok(o) : Results.NotFound());

        api.MapPost("/", async (SaveOccurrenceRequest body, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
        {
            var id = await s.CreateAsync(body.ToInput(), body.Register, ActorOf(user), ct);
            return Results.Created($"/api/occurrences/{id}", new { id });
        });

        api.MapPut("/{id:guid}", async (Guid id, SaveOccurrenceRequest body, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.UpdateAsync(id, body.ToInput(), body.Reason, body.ExpectedVersion, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        api.MapPost("/{id:guid}/register", async (Guid id, VersionRequest body, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.RegisterAsync(id, body.ExpectedVersion, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        api.MapPost("/{id:guid}/status", async (Guid id, StatusRequest body, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.ChangeStatusAsync(id, body.Status, body.Reason, body.ExpectedVersion, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        api.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.DeleteDraftAsync(id, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/{id:guid}/revisions/{version:int}", async (Guid id, int version, ClaimsPrincipal user, OccurrenceService s, CancellationToken ct) =>
            await s.GetRevisionAsync(id, version, ActorOf(user).Id, ct) is { } r ? Results.Ok(r) : Results.NotFound());

        // ---- Anexos e ata ----
        api.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, ClaimsPrincipal user, OccurrenceDocuments d, CancellationToken ct) =>
        {
            if (file.Length is 0 or > DocumentTypes.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Envie PDF, PNG ou JPG de até 10 MB."] });
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var created = await d.UploadAsync(id, file.FileName, buffer.ToArray(), ActorOf(user), ct);
            return created is null ? Results.NotFound() : Results.Created($"/api/occurrences/{id}/attachments/{created.Id}", created);
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimit(DocumentTypes.MaxBytes + 64_000));

        api.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, ClaimsPrincipal user, OccurrenceDocuments d, CancellationToken ct) =>
            await d.DownloadAsync(id, attachmentId, ActorOf(user).Id, ct) is { } f ? Results.File(f.Content, f.ContentType, f.FileName) : Results.NotFound());

        api.MapPost("/{id:guid}/attachments/{attachmentId:guid}/remove", async (Guid id, Guid attachmentId, ClaimsPrincipal user, OccurrenceDocuments d, CancellationToken ct) =>
            await d.RemoveAsync(id, attachmentId, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/{id:guid}/ata", async (Guid id, ClaimsPrincipal user, OccurrenceDocuments d, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return await d.AtaAsync(id, ActorOf(user), ct) is { } f ? Results.File(f.Content, f.ContentType, f.FileName) : Results.NotFound();
        });
    }

    private static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (OccurrenceValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(ex.Errors));
        }
        catch (OccurrenceConflictException ex)
        {
            return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static Actor ActorOf(ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue("sub")!), user.FindFirstValue("name") ?? user.FindFirstValue("email") ?? "Equipe");
}

/// <summary>Limite de corpo por endpoint (uploads).</summary>
public sealed class RequestSizeLimit(long bytes) : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = bytes;
}
