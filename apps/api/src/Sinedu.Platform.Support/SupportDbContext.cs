using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Platform.Support;

public enum SupportRequestStatus
{
    /// <summary>Aguardando o Diretor da escola.</summary>
    Pending,
    Approved,
    Denied,
    /// <summary>Retirado pelo próprio suporte antes da decisão.</summary>
    Cancelled,
    /// <summary>Revogado pelo Diretor antes do prazo.</summary>
    Revoked,
}

/// <summary>
/// Pedido de acesso de suporte a uma escola (P-11, D17). A decisão é tomada na escola, pelo Diretor, e espelhada aqui;
/// o motivo é escrito pelo suporte e nunca deve conter dados de aluno.
/// </summary>
public sealed class SupportAccessRequest
{
    public const int MaxHours = 24;
    public const int ReasonMaxLength = 500;

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string TenantSlug { get; set; }

    public required string TenantName { get; set; }

    public Guid RequestedBy { get; set; }

    public required string RequesterName { get; set; }

    public required string RequesterEmail { get; set; }

    public required string Reason { get; set; }

    public int DurationHours { get; set; }

    public SupportRequestStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Nome de quem decidiu na escola (exibido no painel; o id fica na auditoria da escola).</summary>
    public string? DecidedByName { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => Status == SupportRequestStatus.Approved && ExpiresAt > now;

    /// <summary>Situação exibida: "Expired" quando aprovado e o prazo passou.</summary>
    public string DisplayStatus(DateTimeOffset now) =>
        Status == SupportRequestStatus.Approved && ExpiresAt <= now ? "Expired" : Status.ToString();
}

public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options)
{
    public const string Schema = "support";

    public DbSet<SupportAccessRequest> Requests => Set<SupportAccessRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SupportAccessRequest>(e =>
        {
            e.ToTable("access_requests");
            e.Property(x => x.TenantSlug).HasMaxLength(63);
            e.Property(x => x.TenantName).HasMaxLength(200);
            e.Property(x => x.RequesterName).HasMaxLength(200);
            e.Property(x => x.RequesterEmail).HasMaxLength(254);
            e.Property(x => x.Reason).HasMaxLength(SupportAccessRequest.ReasonMaxLength);
            e.Property(x => x.DecidedByName).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.RequestedBy, x.Status });
        });
    }
}

public sealed class SupportDbContextDesignFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SupportDbContext>().UseSineduPostgres("Host=localhost;Database=design", SupportDbContext.Schema).Options);
}
