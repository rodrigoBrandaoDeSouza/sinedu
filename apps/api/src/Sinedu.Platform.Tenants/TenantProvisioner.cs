using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Tenants;

public sealed class TenantDatabaseOptions
{
    public const string Section = "TenantDatabases";

    /// <summary>Conexão com permissão de CREATE ROLE/CREATE DATABASE. Usada só pelo provisionamento.</summary>
    public string AdminConnectionString { get; set; } = "";

    /// <summary>Host/porta que a aplicação da escola usa para se conectar (ex.: PgBouncer em produção).</summary>
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5432;
}

/// <summary>
/// Provisiona uma escola em passos idempotentes: banco e papéis, schema, chave no Vault, bucket,
/// primeiro Diretor e ativação. Pode ser reexecutado após falha; passos concluídos são pulados.
/// </summary>
public sealed partial class TenantProvisioner(
    TenantsDbContext db,
    ISecretStore secrets,
    IKeyManagement keys,
    IObjectStorage storage,
    ITenantSchemaMigrator migrator,
    IAccountInvitations invitations,
    IAuditLog audit,
    IOptions<TenantDatabaseOptions> options,
    TimeProvider clock,
    ILogger<TenantProvisioner> logger)
{
    public async Task ProvisionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.Include(t => t.ProvisioningSteps).FirstAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant.Status is not (TenantStatus.Provisioning or TenantStatus.ProvisioningFailed))
        {
            return;
        }

        tenant.Status = TenantStatus.Provisioning;
        tenant.StatusReason = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await RunStepAsync(tenant, ProvisioningStepName.Database, () => CreateDatabaseAsync(tenant, cancellationToken), cancellationToken);
            await RunStepAsync(tenant, ProvisioningStepName.Schema, () => MigrateAsync(tenant, cancellationToken), cancellationToken);
            await RunStepAsync(tenant, ProvisioningStepName.EncryptionKey, () => keys.CreateKeyAsync(tenant.TransitKeyName, cancellationToken), cancellationToken);
            await RunStepAsync(tenant, ProvisioningStepName.Storage, () => storage.EnsureBucketAsync(tenant.BucketName, cancellationToken), cancellationToken);
            await RunStepAsync(tenant, ProvisioningStepName.FirstDirector, () => AddFirstDirectorAsync(tenant, cancellationToken), cancellationToken);
            await RunStepAsync(tenant, ProvisioningStepName.Activate, () =>
            {
                tenant.Status = TenantStatus.Active;
                tenant.UpdatedAt = clock.GetUtcNow();
                return Task.CompletedTask;
            }, cancellationToken);

            await audit.WriteAsync(new AuditEntry("tenant_provisioned", ResourceType: "tenant", ResourceId: tenant.Id.ToString()), cancellationToken);
            LogProvisioned(logger, tenant.Slug);
        }
#pragma warning disable CA1031 // Qualquer falha marca a escola para nova tentativa pelo painel.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            tenant.Status = TenantStatus.ProvisioningFailed;
            tenant.StatusReason = Sanitize(ex);
            tenant.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            LogFailed(logger, ex, tenant.Slug);
        }
    }

    /// <summary>Atualiza o schema de uma escola já ativa (painel → migrations em lote).</summary>
    public async Task MigrateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var credentials = await ReadCredentialsAsync(tenant, cancellationToken);
        await migrator.MigrateAsync(OwnerConnection(credentials), tenant.AppRole, cancellationToken);
        tenant.SchemaVersion = TenantSchema.Version;
        tenant.UpdatedAt = clock.GetUtcNow();
    }

    private async Task RunStepAsync(Tenant tenant, ProvisioningStepName name, Func<Task> action, CancellationToken cancellationToken)
    {
        if (tenant.ProvisioningSteps.Any(s => s.Step == name && s.Status == ProvisioningStepStatus.Succeeded))
        {
            return;
        }

        var step = new ProvisioningStep
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Step = name,
            Status = ProvisioningStepStatus.Running,
            StartedAt = clock.GetUtcNow(),
        };
        db.ProvisioningSteps.Add(step);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await action();
            step.Status = ProvisioningStepStatus.Succeeded;
        }
        catch (Exception ex)
        {
            step.Status = ProvisioningStepStatus.Failed;
            step.Error = Sanitize(ex);
            throw;
        }
        finally
        {
            step.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task CreateDatabaseAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        // Credenciais são gravadas no Vault antes de criar os papéis: uma nova tentativa reaproveita as mesmas senhas.
        var credentials = await secrets.ReadAsync(tenant.SecretPath, cancellationToken);
        if (credentials is null)
        {
            credentials = new Dictionary<string, string>
            {
                [TenantSecretKeys.Host] = options.Value.Host,
                [TenantSecretKeys.Port] = options.Value.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [TenantSecretKeys.Database] = tenant.DatabaseName,
                [TenantSecretKeys.OwnerUser] = tenant.OwnerRole,
                [TenantSecretKeys.OwnerPassword] = NewPassword(),
                [TenantSecretKeys.AppUser] = tenant.AppRole,
                [TenantSecretKeys.AppPassword] = NewPassword(),
            };
            await secrets.WriteAsync(tenant.SecretPath, credentials, cancellationToken);
        }

        await using var connection = new NpgsqlConnection(options.Value.AdminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureRoleAsync(connection, tenant.OwnerRole, credentials[TenantSecretKeys.OwnerPassword], cancellationToken);
        await EnsureRoleAsync(connection, tenant.AppRole, credentials[TenantSecretKeys.AppPassword], cancellationToken);

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", tenant.DatabaseName);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
            {
                await ExecuteAsync(connection, $"CREATE DATABASE {Q(tenant.DatabaseName)} OWNER {Q(tenant.OwnerRole)}", cancellationToken);
            }
        }

        await ExecuteAsync(connection, $"REVOKE ALL ON DATABASE {Q(tenant.DatabaseName)} FROM PUBLIC", cancellationToken);
        await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {Q(tenant.DatabaseName)} TO {Q(tenant.AppRole)}", cancellationToken);
    }

    private async Task AddFirstDirectorAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var userId = await invitations.InviteToTenantAsync(
            tenant.Id, tenant.Slug, tenant.DisplayName, tenant.FirstDirectorEmail, tenant.FirstDirectorName, privileged: true, cancellationToken);

        var credentials = await ReadCredentialsAsync(tenant, cancellationToken);
        await migrator.AddInitialDirectorAsync(
            OwnerConnection(credentials),
            new InitialDirector(userId, tenant.FirstDirectorEmail, tenant.FirstDirectorName),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadCredentialsAsync(Tenant tenant, CancellationToken cancellationToken) =>
        await secrets.ReadAsync(tenant.SecretPath, cancellationToken)
        ?? throw new InvalidOperationException("Credenciais da escola ausentes no Vault.");

    private static string OwnerConnection(IReadOnlyDictionary<string, string> c) =>
        PersistenceExtensions.BuildConnectionString(
            c[TenantSecretKeys.Host],
            int.Parse(c[TenantSecretKeys.Port], System.Globalization.CultureInfo.InvariantCulture),
            c[TenantSecretKeys.Database],
            c[TenantSecretKeys.OwnerUser],
            c[TenantSecretKeys.OwnerPassword]);

    private static async Task EnsureRoleAsync(NpgsqlConnection connection, string role, string password, CancellationToken cancellationToken)
    {
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @name", connection);
        exists.Parameters.AddWithValue("name", role);
        var verb = await exists.ExecuteScalarAsync(cancellationToken) is null ? "CREATE" : "ALTER";
        await ExecuteAsync(connection, $"{verb} ROLE {Q(role)} LOGIN PASSWORD {PersistenceExtensions.QuoteLiteral(password)}", cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // Identificadores vêm de slug validado e são citados; senhas vêm do gerador.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Q(string identifier) => PersistenceExtensions.QuoteIdentifier(identifier);

    private static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Mensagem de erro sem segredos: tipo + primeira linha, truncada.</summary>
    private static string Sanitize(Exception ex)
    {
        var message = ex.Message.Split('\n')[0];
        if (message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            message = "falha de autenticação";
        }

        var text = $"{ex.GetType().Name}: {message}";
        return text.Length > 500 ? text[..500] : text;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Escola {Slug} provisionada")]
    private static partial void LogProvisioned(ILogger logger, string slug);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao provisionar a escola {Slug}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string slug);
}
