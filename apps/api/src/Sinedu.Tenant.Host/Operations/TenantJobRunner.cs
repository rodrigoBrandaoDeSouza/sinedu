using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Host.Tenancy;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Roda uma tarefa periódica em cada escola ativa, cada uma no próprio escopo (conexão, chave e bucket dela),
/// como numa requisição. Uma escola com problema não para as outras; o log não leva dados pessoais.
/// </summary>
internal sealed class TenantJobRunner(IServiceProvider root, RegistryDataSource registry, ILogger<TenantJobRunner> logger)
{
    public async Task ForEachActiveTenantAsync(string job, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct)
    {
        foreach (var slug in await ActiveSlugsAsync(ct))
        {
            try
            {
                await using var scope = root.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug, ct);
                if (resolution.Status != TenantResolutionStatus.Ready)
                {
                    continue;
                }

                sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
                await work(sp, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("{Job} falhou na escola {Tenant}: {Error}", job, slug, ex.GetType().Name);
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
