using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Host.Tenancy;
using Sinedu.Tenant.Notifications;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Fila de comunicações: a cada minuto, para cada escola ativa, reenvia o que falhou e já pode tentar de novo.
/// Cada escola é processada no próprio escopo (conexão, chave e bucket dela), como numa requisição.
/// Desligável por configuração (Notifications:RetryWorker = false), como nos testes.
/// </summary>
internal sealed class NotificationRetryWorker(IServiceProvider root, RegistryDataSource registry, IConfiguration configuration, ILogger<NotificationRetryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:RetryWorker", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var slug in await ActiveSlugsAsync(stoppingToken))
            {
                try
                {
                    await using var scope = root.CreateAsyncScope();
                    var sp = scope.ServiceProvider;
                    var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug, stoppingToken);
                    if (resolution.Status != TenantResolutionStatus.Ready)
                    {
                        continue;
                    }

                    sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
                    var processed = await sp.GetRequiredService<INotificationRetries>().ProcessDueAsync(stoppingToken);
                    if (processed > 0)
                    {
                        logger.LogInformation("Fila de comunicações: {Count} reenvio(s) na escola {Tenant}", processed, slug);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Uma escola com problema não para as outras. Sem dados pessoais no log.
                    logger.LogWarning("Fila de comunicações falhou na escola {Tenant}: {Error}", slug, ex.GetType().Name);
                }
            }
        }
    }

    private async Task<List<string>> ActiveSlugsAsync(CancellationToken ct)
    {
        var slugs = new List<string>();
        await using var command = registry.DataSource.CreateCommand("SELECT slug FROM tenants.v_registry WHERE status = 'Active'");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            slugs.Add(reader.GetString(0));
        }

        return slugs;
    }
}
