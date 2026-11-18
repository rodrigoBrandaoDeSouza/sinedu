using Microsoft.EntityFrameworkCore;

namespace Sinedu.SharedKernel.Audit;

public enum AuditResult
{
    Allowed,
    Denied,
}

/// <summary>
/// Evento de auditoria. Registra quem fez o quê, quando e com qual resultado — nunca o conteúdo.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? UserId { get; set; }

    public Guid? StudentId { get; set; }

    public required string Action { get; set; }

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public AuditResult Result { get; set; }

    public bool IsSupportSession { get; set; }

    public string? Ip { get; set; }

    public string? CorrelationId { get; set; }
}

public sealed record AuditEntry(
    string Action,
    AuditResult Result = AuditResult.Allowed,
    string? ResourceType = null,
    string? ResourceId = null,
    Guid? StudentId = null,
    Guid? UserId = null);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tabela append-only. O papel de banco da aplicação só tem INSERT/SELECT e um trigger bloqueia
/// UPDATE/DELETE (ver migration inicial).
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.ResourceType).HasMaxLength(60);
            e.Property(x => x.ResourceId).HasMaxLength(100);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.StudentId, x.OccurredAt });
            e.HasIndex(x => new { x.UserId, x.OccurredAt });
        });
    }
}
