using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Imports;

public sealed class ImportsModule : IModule
{
    public string Name => "Imports";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<ImportsDbContext>(ImportsDbContext.Schema);
        services.AddScoped<ImportService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // P8: importação comum por Diretor e Admin técnico.
        var api = endpoints.MapGroup("/api/imports").RequireAuthorization(Policies.ImportCommon);

        api.MapGet("/fields", () => ImportFields.All);

        api.MapGet("/", async (ImportsDbContext db, CancellationToken ct) =>
        {
            var batches = await db.Batches.AsNoTracking().Where(b => b.Status != ImportStatus.Cancelled).OrderByDescending(b => b.CreatedAt).Take(50).ToListAsync(ct);
            return batches.Select(b => new
            {
                b.Id, b.FileName, b.Status, b.Sheet, b.CreatedAt, b.CompletedAt, b.CreatedBy, Totals = ImportService.Totals(b),
            });
        });

        api.MapPost("/", async (IFormFile file, ClaimsPrincipal user, ImportService imports, CancellationToken ct) =>
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var batch = await imports.UploadAsync(stream, file.FileName, UserId(user), ct);
                return Results.Created($"/api/imports/{batch.Id}", new { batch.Id });
            }
            catch (SpreadsheetException ex)
            {
                return Problem(ex.Message);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.IO.IOException or FormatException or InvalidOperationException)
            {
                return Problem("Não foi possível ler o arquivo. Confira se ele abre no Excel e salve novamente como .xlsx.");
            }
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttributeShim(SpreadsheetReader.MaxBytes + 64_000));

        api.MapGet("/{id:guid}", async (Guid id, string? sheet, int? headerRow, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null) return Results.NotFound();

            var sheets = ImportService.Sheets(batch);
            var selected = sheets.FirstOrDefault(s => s.Name == (sheet ?? batch.Sheet)) ?? sheets.OrderByDescending(s => s.RowCount).First();
            var header = headerRow ?? (selected.Name == batch.Sheet ? batch.HeaderRow : null) ?? selected.HeaderRow;
            var (mapping, savedMapping) = selected.Name == batch.Sheet && batch.MappingJson is not null && headerRow is null
                ? (ImportService.Mapping(batch), false)
                : await imports.SuggestAsync(batch, selected.Name, header, ct);

            var headers = await db.Rows.AsNoTracking().Where(r => r.BatchId == id && r.Sheet == selected.Name && r.RowNumber == header).Select(r => r.Cells).FirstOrDefaultAsync(ct) ?? [];
            var sample = await db.Rows.AsNoTracking().Where(r => r.BatchId == id && r.Sheet == selected.Name && r.RowNumber > header)
                .OrderBy(r => r.RowNumber).Take(5).Select(r => r.Cells).ToListAsync(ct);

            return Results.Ok(new
            {
                batch.Id, batch.FileName, batch.Status, batch.CreatedAt, batch.CompletedAt,
                Sheets = sheets.Select(s => new { s.Name, s.RowCount }),
                Sheet = selected.Name,
                HeaderRow = header,
                Headers = headers,
                Sample = sample,
                Mapping = mapping,
                SavedMapping = savedMapping,
                Totals = ImportService.Totals(batch),
            });
        });

        api.MapPut("/{id:guid}/mapping", async (Guid id, MappingRequest body, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null) return Results.NotFound();
            try
            {
                return Results.Ok(await imports.MapAndValidateAsync(batch, body.Sheet, body.HeaderRow, body.Mapping, ct));
            }
            catch (ImportException ex)
            {
                return Problem(ex.Message);
            }
        });

        api.MapGet("/{id:guid}/rows", async (
            Guid id, RowAction? action, bool? pendingOnly, int? page, ImportsDbContext db, IStudentRegistry students, IClassroomDirectory classrooms, CancellationToken ct) =>
        {
            var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch?.MappingJson is null) return Results.NotFound();

            var query = db.Rows.AsNoTracking().Where(r => r.BatchId == id && r.Action != RowAction.Empty && r.RowNumber > batch.HeaderRow);
            if (action is { } a) query = query.Where(r => r.Action == a);
            if (pendingOnly == true) query = query.Where(r => r.Decision == RowDecision.None && (r.Action == RowAction.PossibleDuplicate || r.Action == RowAction.Conflict));

            var total = await query.CountAsync(ct);
            var size = 50;
            var current = Math.Max(page ?? 1, 1);
            var rows = await query.OrderBy(r => r.RowNumber).Skip((current - 1) * size).Take(size).ToListAsync(ct);

            var related = await students.GetAsync(rows.SelectMany(r => r.Candidates.Append(r.TargetStudentId ?? Guid.Empty)).Where(g => g != Guid.Empty).Distinct().ToList(), ct);
            var names = await classrooms.GetAsync(related.Where(s => s.ActiveClassroomId != null).Select(s => s.ActiveClassroomId!.Value), ct);
            object View(StudentSnapshot s) => new { s.Id, Name = s.FullName, Code = s.InstitutionalCode, s.BirthDate, Classroom = s.ActiveClassroomId is { } c ? names.GetValueOrDefault(c)?.Name : null };

            var mapping = ImportService.Mapping(batch);
            return Results.Ok(new
            {
                total,
                page = current,
                pageSize = size,
                items = rows.Select(r =>
                {
                    var p = ImportService.Parse(r.Cells, mapping);
                    return new
                    {
                        r.Id, r.RowNumber, r.Action, r.Decision, r.Outcome, r.Errors, r.Warnings, r.Changes,
                        Name = p.Name, Code = p.Code, Classroom = p.Classroom?.Display, p.BirthDate,
                        Existing = r.TargetStudentId is { } t ? related.Where(s => s.Id == t).Select(View).FirstOrDefault() : null,
                        Candidates = related.Where(s => r.Candidates.Contains(s.Id)).Select(View),
                    };
                }),
            });
        });

        api.MapPost("/{id:guid}/rows/{rowId:guid}/decision", async (Guid id, Guid rowId, DecisionRequest body, ClaimsPrincipal user, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null) return Results.NotFound();
            try
            {
                await imports.DecideAsync(batch, rowId, body.Decision, body.TargetStudentId, UserId(user), ct);
                return Results.NoContent();
            }
            catch (ImportException ex)
            {
                return Problem(ex.Message);
            }
        });

        api.MapPost("/{id:guid}/skip-pending", async (Guid id, ClaimsPrincipal user, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id && b.Status == ImportStatus.Validated, ct);
            return batch is null ? Results.NotFound() : Results.Ok(new { skipped = await imports.SkipPendingAsync(batch, UserId(user), ct) });
        });

        api.MapPost("/{id:guid}/confirm", async (Guid id, ClaimsPrincipal user, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null) return Results.NotFound();
            try
            {
                return Results.Ok(await imports.ConfirmAsync(batch, UserId(user), ct));
            }
            catch (ImportException ex)
            {
                return Problem(ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Problem(statusCode: 409, title: "Este lote já está sendo processado.");
            }
        });

        api.MapDelete("/{id:guid}", async (Guid id, ImportsDbContext db, ImportService imports, CancellationToken ct) =>
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null) return Results.NotFound();
            try
            {
                await imports.CancelAsync(batch, ct);
                return Results.NoContent();
            }
            catch (ImportException ex)
            {
                return Problem(ex.Message);
            }
        });
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

    private static IResult Problem(string message) =>
        Results.Problem(title: message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = "import_error" });
}

public sealed record MappingRequest(string Sheet, int HeaderRow, Dictionary<ImportField, int> Mapping);

public sealed record DecisionRequest(RowDecision Decision, Guid? TargetStudentId);

/// <summary>Limite de tamanho do corpo do upload.</summary>
public sealed class RequestSizeLimitAttributeShim(long bytes) : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = bytes;
}
