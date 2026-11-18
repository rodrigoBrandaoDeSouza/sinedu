using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Tenants;

/// <summary>Fila em memória de escolas a provisionar. Ao iniciar, retoma as que ficaram pendentes.</summary>
public sealed class ProvisioningScheduler
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid tenantId) => _channel.Writer.WriteAsync(tenantId);

    internal ChannelReader<Guid> Reader => _channel.Reader;
}

internal sealed class ProvisioningWorker(ProvisioningScheduler queue, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantsDbContext>();
            var pending = await db.Tenants
                .Where(t => t.Status == TenantStatus.Provisioning)
                .Select(t => t.Id)
                .ToListAsync(stoppingToken);
            foreach (var id in pending)
            {
                await queue.EnqueueAsync(id);
            }
        }

        await foreach (var tenantId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<TenantProvisioner>();
            await provisioner.ProvisionAsync(tenantId, stoppingToken);
        }
    }
}
