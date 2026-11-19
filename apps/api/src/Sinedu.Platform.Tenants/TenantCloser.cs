using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Tenants;

public enum ClosureResult
{
    Done,
    NotFound,
    WrongStatus,
    RetentionPending,
    ConfirmationMismatch,
}

/// <summary>
/// Encerramento de escola (P-13, decisão P11): a escola fica fechada (só a Direção baixa a exportação dos dados comuns),
/// o banco é mantido pelo prazo de retenção e então destruído junto com anexos, segredos e a chave Transit
/// (crypto-shredding: Saúde/Proteção ficam ilegíveis inclusive em backups). A destruição é manual e confirmada.
/// A plataforma não lê nenhum dado da escola aqui: só DDL e remoção de recursos.
/// </summary>
public sealed class TenantCloser(
    TenantsDbContext db,
    ISecretStore secrets,
    IKeyManagement keys,
    IObjectStorage storage,
    IAuditLog audit,
    IOptions<TenantDatabaseOptions> options,
    IConfiguration configuration,
    TimeProvider clock)
{
    public TimeSpan Retention => TimeSpan.FromDays(configuration.GetValue("Tenants:ClosingRetentionDays", 90));

    public async Task<ClosureResult> StartAsync(Guid id, string reason, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return ClosureResult.NotFound;
        if (tenant.Status is not (TenantStatus.Active or TenantStatus.Suspended)) return ClosureResult.WrongStatus;

        var now = clock.GetUtcNow();
        tenant.Status = TenantStatus.Closing;
        tenant.StatusReason = reason.Trim();
        tenant.ClosingStartedAt = now;
        tenant.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("tenant_closing_started", ResourceType: "tenant", ResourceId: id.ToString()), ct);
        return ClosureResult.Done;
    }

    /// <summary>Desfaz o encerramento antes da destruição: a escola volta como suspensa (reativar é outro passo).</summary>
    public async Task<ClosureResult> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return ClosureResult.NotFound;
        if (tenant.Status != TenantStatus.Closing) return ClosureResult.WrongStatus;

        var now = clock.GetUtcNow();
        tenant.Status = TenantStatus.Suspended;
        tenant.StatusReason = reason.Trim();
        tenant.SuspendedAt = now;
        tenant.ClosingStartedAt = null;
        tenant.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("tenant_closing_cancelled", ResourceType: "tenant", ResourceId: id.ToString()), ct);
        return ClosureResult.Done;
    }

    /// <summary>
    /// Destrói os dados da escola. Só depois do prazo de retenção e com o endereço digitado. Passos idempotentes:
    /// se algum falhar, a escola continua "em encerramento" e a destruição pode ser repetida.
    /// </summary>
    public async Task<ClosureResult> DestroyAsync(Guid id, string confirmSlug, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return ClosureResult.NotFound;
        if (tenant.Status != TenantStatus.Closing || tenant.ClosingStartedAt is null) return ClosureResult.WrongStatus;
        if (!string.Equals(confirmSlug?.Trim(), tenant.Slug, StringComparison.Ordinal)) return ClosureResult.ConfirmationMismatch;
        if (clock.GetUtcNow() < tenant.ClosingStartedAt.Value + Retention) return ClosureResult.RetentionPending;

        await DropDatabaseAsync(tenant, ct);
        await storage.DeleteBucketAsync(tenant.BucketName, ct);
        await keys.DestroyKeyAsync(tenant.TransitKeyName, ct);
        await secrets.DeleteAsync(tenant.SecretPath, ct);
        await secrets.DeleteAsync(TenantSmtp.SecretPath(tenant.Id), ct);

        var now = clock.GetUtcNow();
        tenant.Status = TenantStatus.Closed;
        tenant.ClosedAt = now;
        tenant.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("tenant_destroyed", ResourceType: "tenant", ResourceId: id.ToString()), ct);
        return ClosureResult.Done;
    }

    private async Task DropDatabaseAsync(Tenant tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(options.Value.AdminConnectionString);
        await connection.OpenAsync(ct);
        // WITH (FORCE) derruba conexões abertas (PgBouncer, host da escola). Identificadores vêm do slug validado.
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Q(tenant.DatabaseName)} WITH (FORCE)", ct);
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS {Q(tenant.AppRole)}", ct);
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS {Q(tenant.OwnerRole)}", ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
#pragma warning disable CA2100 // Identificadores citados, derivados de slug validado.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Q(string identifier) => PersistenceExtensions.QuoteIdentifier(identifier);
}
