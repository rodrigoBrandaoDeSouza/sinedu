using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Pedidos de acesso de suporte (P-11): a cada minuto, cada escola ativa busca os próprios pedidos na plataforma e
/// avisa os diretores por e-mail dos novos. A decisão continua sendo só do Diretor, na tela da escola.
/// Desligável por configuração (Support:Worker = false), como nos testes.
/// </summary>
internal sealed class SupportRequestWorker(TenantJobRunner runner, IConfiguration configuration) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Support:Worker", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await runner.ForEachActiveTenantAsync("Pedidos de suporte",
                (sp, ct) => sp.GetRequiredService<SupportAccessService>().SyncAsync(ct), stoppingToken);
        }
    }
}
