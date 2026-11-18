namespace Sinedu.SharedKernel.Tenancy;

/// <summary>
/// Versão de schema esperada para os bancos de escola. Incrementar sempre que uma migration
/// de módulo da escola for adicionada. A aplicação recusa escolas em versão diferente.
/// </summary>
public static class TenantSchema
{
    public const string Version = "2026.11.19.1";
}

public enum TenantStatus
{
    Provisioning,
    ProvisioningFailed,
    Active,
    Suspended,
    Closing,
    Closed,
}

/// <summary>Entrada do registro de escolas (sem segredos — credenciais ficam no Vault).</summary>
public sealed record TenantRegistration(
    Guid Id,
    string Slug,
    string DisplayName,
    TenantStatus Status,
    string? SchemaVersion,
    string DatabaseName,
    string SecretPath,
    string TransitKeyName,
    string BucketName);

/// <summary>Consulta ao registro de escolas. Implementado pela plataforma (escrita) e pelo host da escola (leitura).</summary>
public interface ITenantDirectory
{
    Task<TenantRegistration?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>Credenciais de banco de uma escola, guardadas no Vault em <c>tenants/{id}</c>.</summary>
public static class TenantSecretKeys
{
    public const string Host = "host";
    public const string Port = "port";
    public const string Database = "database";
    public const string OwnerUser = "owner_user";
    public const string OwnerPassword = "owner_password";
    public const string AppUser = "app_user";
    public const string AppPassword = "app_password";
}

/// <summary>Primeiro Diretor gravado no banco da escola pelo provisionamento.</summary>
public sealed record InitialDirector(Guid UserId, string Email, string FullName);

/// <summary>
/// Aplica migrations de todos os módulos da escola num banco e concede permissões ao papel da aplicação.
/// Implementado por <c>Sinedu.Tenant.Migrations</c> e chamado pela plataforma.
/// </summary>
public interface ITenantSchemaMigrator
{
    Task MigrateAsync(string ownerConnectionString, string appRole, CancellationToken cancellationToken = default);

    /// <summary>Grava o primeiro Diretor (só quando a escola não tem Diretor ativo).</summary>
    Task AddInitialDirectorAsync(string ownerConnectionString, InitialDirector director, CancellationToken cancellationToken = default);
}

/// <summary>Convites de conta. Implementado pelo módulo de identidade da plataforma.</summary>
public interface IAccountInvitations
{
    /// <summary>Garante a conta, o vínculo com a escola e envia convite (conta nova) ou aviso (conta existente).</summary>
    Task<Guid> InviteToTenantAsync(Guid tenantId, string tenantSlug, string tenantName, string email, string fullName, bool privileged, CancellationToken cancellationToken = default);
}

/// <summary>Contrato das métricas da plataforma (P-10): papel de banco e colunas da view <c>metrics.v_counts</c>.</summary>
public static class MetricsContract
{
    /// <summary>Papel de banco que só tem SELECT na view de contagens de cada escola.</summary>
    public const string Role = "sinedu_metrics";

    /// <summary>Senha do papel, no Vault (KV), gerada pela plataforma.</summary>
    public const string SecretPath = "platform/metrics";
}
