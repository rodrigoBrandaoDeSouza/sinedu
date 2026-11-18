using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Imports;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed record SaveHealthRequest(HealthData? Data, DateOnly? ReviewDueOn, string? Reason, int? ExpectedVersion);

public sealed record SaveProtectionRequest(ProtectionKind Kind, DateOnly? ValidUntil, ProtectionData? Data, string? Reason, int? ExpectedVersion);

public sealed record RevokeRequest(string? Reason, int ExpectedVersion);

public sealed record ExportRequest(bool Health, bool Protection, string? Justification);

/// <summary>
/// Saúde e Proteção/Sigilo (Fase 4). Rotas, DTOs e política próprios: nenhum endpoint comum de aluno conhece este módulo.
/// Toda rota exige DIRETOR/PEDAGOGO + MFA + autenticação forte ≤ 15 min, responde com no-store e 404 para aluno de fora.
/// </summary>
public sealed class ProtectedRecordsModule : IModule
{
    public string Name => "ProtectedRecords";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<ProtectedDbContext>(ProtectedDbContext.Schema);
        services.AddScoped<ProtectedCrypto>();
        services.AddScoped<ProtectedService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<ExportService>();
        services.AddScoped<HealthImport>();
        services.AddScoped<KeyRewrap>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var student = Protected(endpoints.MapGroup("/api/students/{studentId:guid}"))
            .AddEndpointFilter(async (ctx, next) =>
            {
                // Aluno que não existe NESTA escola → 404 (mesma resposta para "não existe" e "é de outra escola").
                if (!Guid.TryParse(ctx.HttpContext.GetRouteValue("studentId")?.ToString(), out var id))
                {
                    return Results.NotFound();
                }

                var service = ctx.HttpContext.RequestServices.GetRequiredService<ProtectedService>();
                return await service.StudentExistsAsync(id, ctx.HttpContext.RequestAborted) ? await next(ctx) : Results.NotFound();
            });

        // ---- Saúde ----
        student.MapGet("/health", async (Guid studentId, ProtectedService s, CancellationToken ct) =>
            Results.Ok(new { record = await s.GetHealthAsync(studentId, ct) }));

        student.MapPut("/health", async (Guid studentId, SaveHealthRequest body, ClaimsPrincipal user, ProtectedService s, CancellationToken ct) =>
            Results.Ok(await s.SaveHealthAsync(studentId, body.Data ?? Empty, body.ReviewDueOn, body.Reason, body.ExpectedVersion, ActorOf(user), ct)));

        student.MapGet("/health/versions", async (Guid studentId, ProtectedService s, CancellationToken ct) =>
            await s.ListVersionsAsync(studentId, RecordKind.Health, null, ct) is { } list ? Results.Ok(list) : Results.NotFound());

        student.MapGet("/health/versions/{version:int}", async (Guid studentId, int version, ProtectedService s, CancellationToken ct) =>
            await s.GetVersionAsync<HealthData>(studentId, RecordKind.Health, null, version, ct) is { } v
                ? Results.Ok(new HealthVersionView(v.Version.Version, v.Version.CreatedAt, v.Version.CreatedByName, v.Reason, v.Data))
                : Results.NotFound());

        // ---- Proteção/Sigilo ----
        student.MapGet("/protection", async (Guid studentId, ProtectedService s, CancellationToken ct) =>
            Results.Ok(await s.ListProtectionAsync(studentId, ct)));

        student.MapPost("/protection", async (Guid studentId, SaveProtectionRequest body, ClaimsPrincipal user, ProtectedService s, CancellationToken ct) =>
        {
            var created = await s.CreateProtectionAsync(studentId, body.Kind, body.ValidUntil, body.Data ?? EmptyProtection, body.Reason, ActorOf(user), ct);
            return Results.Created($"/api/students/{studentId}/protection/{created.Id}", created);
        });

        student.MapPut("/protection/{recordId:guid}", async (Guid studentId, Guid recordId, SaveProtectionRequest body, ClaimsPrincipal user, ProtectedService s, CancellationToken ct) =>
            await s.UpdateProtectionAsync(studentId, recordId, body.Kind, body.ValidUntil, body.Data ?? EmptyProtection, body.Reason, body.ExpectedVersion ?? -1, ActorOf(user), ct) is { } v
                ? Results.Ok(v)
                : Results.NotFound());

        student.MapPost("/protection/{recordId:guid}/revoke", async (Guid studentId, Guid recordId, RevokeRequest body, ClaimsPrincipal user, ProtectedService s, CancellationToken ct) =>
            await s.RevokeProtectionAsync(studentId, recordId, body.Reason, body.ExpectedVersion, ActorOf(user), ct) is { } v ? Results.Ok(v) : Results.NotFound());

        student.MapGet("/protection/{recordId:guid}/versions", async (Guid studentId, Guid recordId, ProtectedService s, CancellationToken ct) =>
            await s.ListVersionsAsync(studentId, RecordKind.Protection, recordId, ct) is { } list ? Results.Ok(list) : Results.NotFound());

        student.MapGet("/protection/{recordId:guid}/versions/{version:int}", async (Guid studentId, Guid recordId, int version, ProtectedService s, CancellationToken ct) =>
            await s.GetVersionAsync<ProtectionData>(studentId, RecordKind.Protection, recordId, version, ct) is { } v
                ? Results.Ok(new ProtectionVersionView(v.Version.Version, v.Version.CreatedAt, v.Version.CreatedByName, v.Reason, v.Data))
                : Results.NotFound());

        // ---- Anexos protegidos ----
        student.MapGet("/protected-attachments", async (Guid studentId, RecordKind? owner, Guid? ownerId, AttachmentService a, CancellationToken ct) =>
            Results.Ok(await a.ListAsync(studentId, owner, ownerId, ct)));

        student.MapPost("/protected-attachments", async (Guid studentId, HttpRequest request, ClaimsPrincipal user, AttachmentService a, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || !Enum.TryParse<RecordKind>(form["owner"], true, out var owner) || !Guid.TryParse(form["ownerId"], out var ownerId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Envie o arquivo e o registro a que ele pertence."] });
            }

            if (file.Length > AttachmentService.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Envie um arquivo de até 10 MB."] });
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var created = await a.UploadAsync(studentId, owner, ownerId, file.FileName, form["description"], buffer.ToArray(), ActorOf(user), ct);
            return Results.Created($"/api/students/{studentId}/protected-attachments/{created.Id}", created);
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttributeShim(AttachmentService.MaxBytes + 64_000));

        student.MapGet("/protected-attachments/{attachmentId:guid}", async (Guid studentId, Guid attachmentId, AttachmentService a, CancellationToken ct) =>
            await a.DownloadAsync(studentId, attachmentId, ct) is { } file
                ? Results.File(file.Content, file.ContentType, file.FileName)
                : Results.NotFound());

        student.MapPost("/protected-attachments/{attachmentId:guid}/remove", async (Guid studentId, Guid attachmentId, ClaimsPrincipal user, AttachmentService a, CancellationToken ct) =>
            await a.RemoveAsync(studentId, attachmentId, ActorOf(user), ct) ? Results.NoContent() : Results.NotFound());

        // ---- Exportação excepcional ----
        student.MapPost("/protected-export", async (Guid studentId, ExportRequest body, ClaimsPrincipal user, ExportService e, CancellationToken ct) =>
        {
            var (pdf, exportId) = await e.ExportAsync(studentId, body.Health, body.Protection, body.Justification, ActorOf(user), ct);
            return Results.File(pdf, "application/pdf", $"exportacao-protegida-{exportId.ToString("N")[..8]}.pdf");
        });

        // ---- Visões da escola (sem aluno na rota) ----
        var school = Protected(endpoints.MapGroup("/api/protected"));

        school.MapGet("/pending", async (ProtectedService s, CancellationToken ct) => Results.Ok(await s.PendingAsync(ct)));

        school.MapGet("/health-import/columns", () => Results.Ok(HealthImport.ExpectedHeaders));

        school.MapPost("/health-import", async (HttpRequest request, ClaimsPrincipal user, HealthImport import, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > SpreadsheetReader.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Envie uma planilha .xlsx ou .csv de até 10 MB."] });
            }

            var dryRun = !string.Equals(form["confirm"], "true", StringComparison.OrdinalIgnoreCase);
            try
            {
                await using var stream = file.OpenReadStream();
                var sheet = form["sheet"].ToString();
                return Results.Ok(await import.RunAsync(stream, file.FileName, sheet.Length == 0 ? null : sheet, dryRun, form["reason"], ActorOf(user), ct));
            }
            catch (SpreadsheetException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
            {
                return Results.Problem(title: "Não foi possível ler o arquivo. Salve novamente como .xlsx.", statusCode: StatusCodes.Status400BadRequest);
            }
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttributeShim(SpreadsheetReader.MaxBytes + 64_000));
    }

    private static readonly HealthData Empty = new(null, null, null, null, null, null, null, null, null);

    private static readonly ProtectionData EmptyProtection = new("", null, null, null, null, null, null, VerificationLevel.NaoVerificado, null, null, null);

    /// <summary>Política, marcador de recurso protegido, cabeçalhos sem cache e tradução de erros (sem conteúdo).</summary>
    private static RouteGroupBuilder Protected(RouteGroupBuilder group)
    {
        group.RequireAuthorization(Policies.ProtectedAccess)
            .WithMetadata(new ProtectedResourceMetadata())
            .AddEndpointFilter(async (ctx, next) =>
            {
                var headers = ctx.HttpContext.Response.Headers;
                headers.CacheControl = "no-store, max-age=0";
                headers.Pragma = "no-cache";
                headers["X-Robots-Tag"] = "noindex, nofollow";
                try
                {
                    return await next(ctx);
                }
                catch (ProtectedValidationException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>(ex.Errors));
                }
                catch (ProtectedConflictException ex)
                {
                    return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
                }
            });
        return group;
    }

    private static Actor ActorOf(ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue("sub")!), user.FindFirstValue("name") ?? user.FindFirstValue("email") ?? "Equipe");
}
