using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.Host.Tenancy;

/// <summary>Conexão somente leitura com a view de registro da plataforma.</summary>
public sealed class RegistryDataSource(NpgsqlDataSource dataSource)
{
    public NpgsqlDataSource DataSource { get; } = dataSource;
}

/// <summary>
/// Resolve a escola pelo registro da plataforma (view <c>tenants.v_registry</c>, somente leitura) e
/// busca as credenciais do banco dela no Vault. Ambos com cache curto.
/// </summary>
public sealed class TenantRegistryResolver(RegistryDataSource registry, ISecretStore secrets, IMemoryCache cache) : ITenantResolver
{
    private static readonly TimeSpan RegistryTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CredentialsTtl = TimeSpan.FromMinutes(5);

    public async Task<TenantResolution> ResolveAsync(string slug, CancellationToken cancellationToken = default)
    {
        var entry = await cache.GetOrCreateAsync($"tenant:{slug}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = RegistryTtl;
            return await LoadAsync(slug, cancellationToken);
        });

        if (entry is null)
        {
            return new TenantResolution(TenantResolutionStatus.NotFound);
        }

        switch (entry.Status)
        {
            case TenantStatus.Suspended or TenantStatus.Closed:
                return new TenantResolution(TenantResolutionStatus.Unavailable);
            case not (TenantStatus.Active or TenantStatus.Closing):
                return new TenantResolution(TenantResolutionStatus.NotFound);
        }

        if (entry.SchemaVersion != TenantSchema.Version)
        {
            return new TenantResolution(TenantResolutionStatus.Maintenance);
        }

        var connectionString = await cache.GetOrCreateAsync($"tenant-db:{entry.Id}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CredentialsTtl;
            var c = await secrets.ReadAsync(entry.SecretPath, cancellationToken)
                ?? throw new InvalidOperationException("Credenciais da escola ausentes.");
            return PersistenceExtensions.BuildConnectionString(
                c[TenantSecretKeys.Host],
                int.Parse(c[TenantSecretKeys.Port], CultureInfo.InvariantCulture),
                c[TenantSecretKeys.Database],
                c[TenantSecretKeys.AppUser],
                c[TenantSecretKeys.AppPassword]);
        });

        return new TenantResolution(
            TenantResolutionStatus.Ready,
            new TenantContext(entry.Id, entry.Slug, entry.DisplayName, connectionString!, entry.TransitKeyName, entry.BucketName,
                Closing: entry.Status == TenantStatus.Closing));
    }

    private async Task<TenantRegistration?> LoadAsync(string slug, CancellationToken cancellationToken)
    {
        await using var command = registry.DataSource.CreateCommand(
            "SELECT id, slug, display_name, status, schema_version, database_name, secret_path, transit_key_name, bucket_name " +
            "FROM tenants.v_registry WHERE slug = $1");
        command.Parameters.AddWithValue(slug);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TenantRegistration(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<TenantStatus>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8));
    }
}
