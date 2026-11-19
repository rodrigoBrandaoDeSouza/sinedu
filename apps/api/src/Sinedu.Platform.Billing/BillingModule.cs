using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Billing;

/// <summary>
/// Cobrança manual (D16, P-06..P-09): planos por faixa de alunos, contrato por escola com a APMF pagante,
/// fatura mensal com baixa manual e NF opcional, alertas. Planos/contratos/faturas: só SUPER_ADMIN; alertas: equipe.
/// </summary>
public sealed class BillingModule : IModule
{
    public string Name => "Billing";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Platform") ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");
        services.AddDbContext<BillingDbContext>(o => o.UseSineduPostgres(connectionString, BillingDbContext.Schema));
        services.AddScoped<BillingService>();
        services.AddHostedService<InvoiceWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapPanelApi("/billing").RequireAuthorization(Policies.PlatformSuperAdmin);

        // ---- Planos ----
        api.MapGet("/plans", async (BillingDbContext db, CancellationToken ct) =>
        {
            var plans = await db.Plans.AsNoTracking().Include(p => p.Tiers).OrderBy(p => p.Name).ToListAsync(ct);
            var inUse = await db.Contracts.AsNoTracking().Where(c => c.Status == ContractStatus.Active).GroupBy(c => c.PlanId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return Results.Ok(plans.Select(p => new
            {
                p.Id,
                p.Name,
                p.IsActive,
                contracts = inUse.GetValueOrDefault(p.Id),
                tiers = p.Tiers.OrderBy(t => t.UpToStudents ?? int.MaxValue).Select(t => new { t.UpToStudents, t.MonthlyPrice }),
            }));
        });

        api.MapPost("/plans", async (PlanInput body, BillingService billing, BillingDbContext db, CancellationToken ct) =>
        {
            var errors = BillingService.Validate(body);
            if (errors.Count == 0 && await db.Plans.AnyAsync(p => p.Name == body.Name.Trim(), ct)) errors["name"] = ["Já existe um plano com este nome."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var plan = await billing.SavePlanAsync(null, body, ct);
            return Results.Ok(new { plan.Id });
        });

        api.MapPut("/plans/{id:guid}", async (Guid id, PlanInput body, BillingService billing, BillingDbContext db, CancellationToken ct) =>
        {
            if (!await db.Plans.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
            var errors = BillingService.Validate(body);
            if (errors.Count == 0 && await db.Plans.AnyAsync(p => p.Id != id && p.Name == body.Name.Trim(), ct)) errors["name"] = ["Já existe um plano com este nome."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            await billing.SavePlanAsync(id, body, ct);
            return Results.NoContent();
        });

        // ---- Contratos ----
        api.MapGet("/contracts", async (BillingDbContext db, TenantsDbContext tenants, CancellationToken ct) =>
        {
            var names = await tenants.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => new { t.DisplayName, t.Slug }, ct);
            var contracts = await db.Contracts.AsNoTracking().Include(c => c.Plan).OrderBy(c => c.Status).ThenBy(c => c.StartsOn).ToListAsync(ct);
            return Results.Ok(contracts.Select(c => new
            {
                c.Id,
                tenant = new { id = c.TenantId, name = names.GetValueOrDefault(c.TenantId)?.DisplayName ?? "—", slug = names.GetValueOrDefault(c.TenantId)?.Slug },
                plan = new { id = c.PlanId, name = c.Plan!.Name },
                c.PayerName,
                c.PayerDocument,
                c.PayerContactName,
                c.PayerEmail,
                c.PayerPhone,
                c.StartsOn,
                c.EndsOn,
                c.DueDay,
                c.Status,
                c.Notes,
            }));
        });

        api.MapPost("/contracts", async (ContractInput body, BillingService billing, CancellationToken ct) =>
        {
            var errors = await billing.ValidateAsync(body, null, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var contract = await billing.SaveContractAsync(null, body, ct);
            return Results.Ok(new { contract!.Id });
        });

        api.MapPut("/contracts/{id:guid}", async (Guid id, ContractInput body, BillingService billing, CancellationToken ct) =>
        {
            var errors = await billing.ValidateAsync(body, id, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return await billing.SaveContractAsync(id, body, ct) is null ? Results.NotFound() : Results.NoContent();
        });

        // ---- Faturas ----
        api.MapGet("/invoices", async (string? month, InvoiceStatus? status, bool? overdue, BillingDbContext db, TenantsDbContext tenants, BillingService billing, CancellationToken ct) =>
        {
            var today = billing.Today;
            var query = db.Invoices.AsNoTracking();
            if (DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", out var m)) query = query.Where(i => i.ReferenceMonth == m);
            if (status is { } s) query = query.Where(i => i.Status == s);
            if (overdue == true) query = query.Where(i => i.Status == InvoiceStatus.Open && i.DueOn < today);
            var rows = await query.OrderByDescending(i => i.ReferenceMonth).ThenBy(i => i.DueOn).Take(500).ToListAsync(ct);
            var names = await tenants.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);
            var payers = await db.Contracts.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.PayerName, ct);
            return Results.Ok(rows.Select(i => new
            {
                i.Id,
                i.ContractId,
                school = names.GetValueOrDefault(i.TenantId, "—"),
                payer = payers.GetValueOrDefault(i.ContractId, "—"),
                referenceMonth = i.ReferenceMonth.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
                i.Students,
                i.TierLabel,
                i.Amount,
                i.DueOn,
                status = i.IsOverdue(today) ? "Overdue" : i.Status.ToString(),
                i.InvoiceNumber,
                i.IssuedOn,
                i.PaidOn,
                i.PaidAmount,
                i.Notes,
            }));
        });

        api.MapPost("/invoices/generate", async (GenerateRequest? body, BillingService billing, CancellationToken ct) =>
        {
            DateOnly? month = DateOnly.TryParseExact($"{body?.Month}-01", "yyyy-MM-dd", out var m) ? m : null;
            return Results.Ok(await billing.GenerateAsync(month, ct));
        });

        api.MapPost("/invoices/{id:guid}/pay", async (Guid id, PaymentInput body, BillingService billing, CancellationToken ct) =>
        {
            if (body.PaidAmount <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["paidAmount"] = ["Informe o valor recebido."] });
            return await Conflictable(() => billing.PayAsync(id, body, ct));
        });

        api.MapPut("/invoices/{id:guid}/fiscal-note", async (Guid id, FiscalNoteRequest body, BillingService billing, CancellationToken ct) =>
        {
            if (body.InvoiceNumber?.Trim().Length > 60) return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoiceNumber"] = ["Use no máximo 60 caracteres."] });
            return await Conflictable(() => billing.SetFiscalNoteAsync(id, body.InvoiceNumber, body.IssuedOn, ct));
        });

        api.MapPost("/invoices/{id:guid}/cancel", async (Guid id, CancelRequest body, BillingService billing, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length is < 5 or > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Explique o motivo (5 a 500 caracteres)."] });
            }

            return await Conflictable(() => billing.CancelAsync(id, body.Reason, ct));
        });

        // ---- Alertas: SUPER_ADMIN e SUPORTE (ver) ----
        endpoints.MapPanelApi("/billing/alerts").RequireAuthorization(Policies.PlatformStaff)
            .MapGet("/", async (BillingService billing, CancellationToken ct) => Results.Ok(await billing.AlertsAsync(ct)));
    }

    private static async Task<IResult> Conflictable(Func<Task<Invoice?>> action)
    {
        try
        {
            return await action() is null ? Results.NotFound() : Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }
}

public sealed record GenerateRequest(string? Month);

public sealed record FiscalNoteRequest(string? InvoiceNumber, DateOnly? IssuedOn);

public sealed record CancelRequest(string Reason);

/// <summary>
/// Gera as faturas do mês corrente (idempotente) a cada 6 horas: no primeiro dia útil do mês já estão prontas.
/// Desligável (Billing:Worker = false), como nos testes.
/// </summary>
internal sealed partial class InvoiceWorker(IServiceProvider root, IConfiguration configuration, ILogger<InvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Billing:Worker", true))
        {
            return;
        }

        // Espera a primeira coleta de métricas (contagem de alunos) antes de gerar.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = root.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<BillingService>().GenerateAsync(null, stoppingToken);
                if (result.Created > 0) LogGenerated(logger, result.Created, result.Month);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Faturas geradas: {Count} para {Month}.")]
    private static partial void LogGenerated(ILogger logger, int count, DateOnly month);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geração de faturas falhou: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
