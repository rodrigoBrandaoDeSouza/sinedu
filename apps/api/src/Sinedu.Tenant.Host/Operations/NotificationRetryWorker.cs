using Sinedu.Tenant.Notifications;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Fila de comunicações: a cada minuto, para cada escola ativa, reenvia o que falhou e já pode tentar de novo.
/// Desligável por configuração (Notifications:RetryWorker = false), como nos testes.
/// </summary>
internal sealed class NotificationRetryWorker(TenantJobRunner runner, IConfiguration configuration, ILogger<NotificationRetryWorker> logger)
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
            await runner.ForEachActiveTenantAsync("Fila de comunicações", async (sp, ct) =>
            {
                var processed = await sp.GetRequiredService<INotificationRetries>().ProcessDueAsync(ct);
                if (processed > 0)
                {
                    logger.LogInformation("Fila de comunicações: {Count} reenvio(s)", processed);
                }
            }, stoppingToken);
        }
    }
}
