namespace Sinedu.Platform.Identity;

public enum AccountStatus
{
    Invited,
    Active,
    Disabled,
}

public enum AccountKind
{
    /// <summary>Equipe de escola.</summary>
    School,

    /// <summary>Super admin ou suporte da plataforma (D19: não recebe papéis de escola).</summary>
    Platform,
}

public sealed class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>Sempre em minúsculas.</summary>
    public required string Email { get; set; }

    public required string FullName { get; set; }

    public string? PasswordHash { get; set; }

    public AccountStatus Status { get; set; }

    public AccountKind Kind { get; set; }

    public bool MfaEnabled { get; set; }

    /// <summary>Segredo TOTP cifrado pelo Vault (chave platform-secrets).</summary>
    public string? TotpSecretEncrypted { get; set; }

    /// <summary>Último passo TOTP aceito (impede reuso do mesmo código).</summary>
    public long? LastTotpStep { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Muda em troca de senha, reset de MFA ou desativação: invalida sessões existentes.</summary>
    public Guid SecurityStamp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<PlatformRoleGrant> PlatformRoles { get; set; } = [];

    public List<TenantMembership> Memberships { get; set; } = [];
}

public sealed class PlatformRoleGrant
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Apenas "pode entrar na escola X". Os papéis ficam no banco da escola; a escola mantém este vínculo sincronizado.
/// </summary>
public sealed class TenantMembership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public required string TenantSlug { get; set; }

    public required string TenantName { get; set; }

    public bool Active { get; set; }

    /// <summary>Tem papel que exige MFA nessa escola.</summary>
    public bool Privileged { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RecoveryCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string CodeHash { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

public enum AccountTokenPurpose
{
    Invitation,
    PasswordReset,
}

/// <summary>Token de uso único enviado por e-mail. Só o hash é guardado.</summary>
public sealed class AccountToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public AccountTokenPurpose Purpose { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
