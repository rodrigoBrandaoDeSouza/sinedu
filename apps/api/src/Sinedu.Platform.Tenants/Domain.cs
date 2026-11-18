using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Tenants;

public enum NetworkKind
{
    Estadual,
    Municipal,
    Particular,
    Outra,
}

public sealed class Network
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public NetworkKind Kind { get; set; }

    public string? Uf { get; set; }

    public string? Municipality { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Tenant
{
    public Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string DisplayName { get; set; }

    public Guid? NetworkId { get; set; }

    public Network? Network { get; set; }

    public string? InstitutionalCode { get; set; }

    public TenantStatus Status { get; set; }

    public string? StatusReason { get; set; }

    public required string DatabaseName { get; set; }

    public required string SecretPath { get; set; }

    public required string TransitKeyName { get; set; }

    public required string BucketName { get; set; }

    public string? SchemaVersion { get; set; }

    public required string FirstDirectorEmail { get; set; }

    public required string FirstDirectorName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SuspendedAt { get; set; }

    public List<ProvisioningStep> ProvisioningSteps { get; set; } = [];

    /// <summary>Nomes derivados do slug (já validado): banco, chave e bucket próprios da escola.</summary>
    public static Tenant Create(Guid id, string slug, string displayName, string directorEmail, string directorName, DateTimeOffset now) => new()
    {
        Id = id,
        Slug = slug,
        DisplayName = displayName,
        Status = TenantStatus.Provisioning,
        DatabaseName = "sinedu_t_" + slug.Replace('-', '_'),
        SecretPath = $"tenants/{id:N}",
        TransitKeyName = $"tenant-{id:N}",
        BucketName = $"t-{id:N}",
        FirstDirectorEmail = directorEmail,
        FirstDirectorName = directorName,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public string OwnerRole => "t_" + Slug.Replace('-', '_') + "_owner";

    public string AppRole => "t_" + Slug.Replace('-', '_') + "_app";
}

public enum ProvisioningStepName
{
    Database,
    Schema,
    EncryptionKey,
    Storage,
    FirstDirector,
    Activate,
}

public enum ProvisioningStepStatus
{
    Running,
    Succeeded,
    Failed,
}

public sealed class ProvisioningStep
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public ProvisioningStepName Step { get; set; }

    public ProvisioningStepStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Error { get; set; }
}
