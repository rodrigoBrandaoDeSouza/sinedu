using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Metrics;

public sealed record TenantMetricsRow(
    Guid TenantId, string Slug, string Name, string Status, string? SchemaVersion, bool Outdated, DateTimeOffset? CollectedAt,
    int ActiveStudents, int ActiveClassrooms, int ActiveMembers, int OccurrencesLast30Days, int EmailsSentThisMonth, int EmailsFailedOpen,
    long CommonStorageBytes, string? Error);

/// <summary>
/// Métricas agregadas do painel (P-10, P13). Só contagens por escola — o painel nunca recebe nomes ou dados de aluno.
/// </summary>
public sealed class MetricsModule : IModule
{
    public string Name => "Metrics";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Platform") ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");
        services.AddDbContext<MetricsDbContext>(o => o.UseSineduPostgres(connectionString, MetricsDbContext.Schema));
        services.AddScoped<MetricsCollector>();
        services.AddHostedService<MetricsWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapPanelApi("/metrics").RequireAuthorization(Policies.PlatformStaff);

        api.MapGet("/", async (MetricsDbContext db, TenantsDbContext tenants, CancellationToken ct) =>
        {
            var list = await tenants.Tenants.AsNoTracking().OrderBy(t => t.DisplayName).ToListAsync(ct);
            var latest = (await db.Snapshots.AsNoTracking()
                .Where(s => db.Snapshots.Where(x => x.TenantId == s.TenantId).Max(x => x.CollectedAt) == s.CollectedAt)
                .ToListAsync(ct)).ToDictionary(s => s.TenantId);
            var rows = list.Select(t =>
            {
                var s = latest.GetValueOrDefault(t.Id);
                return new TenantMetricsRow(t.Id, t.Slug, t.DisplayName, t.Status.ToString(), t.SchemaVersion,
                    t.SchemaVersion != Sinedu.SharedKernel.Tenancy.TenantSchema.Version, s?.CollectedAt,
                    s?.ActiveStudents ?? 0, s?.ActiveClassrooms ?? 0, s?.ActiveMembers ?? 0, s?.OccurrencesLast30Days ?? 0,
                    s?.EmailsSentThisMonth ?? 0, s?.EmailsFailedOpen ?? 0, s?.CommonStorageBytes ?? 0, s?.Error);
            }).ToList();

            // Evolução: total de alunos ativos por dia de coleta (últimos 30 dias com coleta).
            var trend = (await db.Snapshots.AsNoTracking().Where(s => s.Error == null).ToListAsync(ct))
                .GroupBy(s => DateOnly.FromDateTime(s.CollectedAt.UtcDateTime))
                .Select(g => new { day = g.Key, students = g.GroupBy(x => x.TenantId).Sum(x => x.OrderByDescending(y => y.CollectedAt).First().ActiveStudents) })
                .OrderByDescending(x => x.day).Take(30).OrderBy(x => x.day).ToList();

            return Results.Ok(new
            {
                schemaVersion = Sinedu.SharedKernel.Tenancy.TenantSchema.Version,
                collectedAt = latest.Values.Select(s => (DateTimeOffset?)s.CollectedAt).DefaultIfEmpty().Max(),
                tenants = rows,
                trend,
            });
        });

        api.MapPost("/collect", async (MetricsCollector collector, IAuditLog audit, CancellationToken ct) =>
        {
            var count = await collector.CollectAsync(ct);
            await audit.WriteAsync(new AuditEntry("metrics_collected", ResourceType: "metrics"), ct);
            return Results.Ok(new { tenants = count });
        }).RequireAuthorization(Policies.PlatformSuperAdmin);
    }
}

/// <summary>Coleta automática a cada 6 horas (desligável: Metrics:Worker = false, como nos testes).</summary>
internal sealed partial class MetricsWorker(IServiceProvider root, IConfiguration configuration, ILogger<MetricsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Metrics:Worker", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = root.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<MetricsCollector>().CollectAsync(stoppingToken);
                LogCollected(logger, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Métricas coletadas de {Count} escola(s).")]
    private static partial void LogCollected(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coleta de métricas falhou: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
