using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.Access;

public enum MemberStatus
{
    Active,
    Inactive,
}

/// <summary>Cópia local mínima de quem tem acesso a esta escola (o id é o "sub" do login único).</summary>
public sealed class Member
{
    public Guid UserId { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public MemberStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public List<UserRole> Roles { get; set; } = [];
}

public sealed class UserRole
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Role { get; set; }

    /// <summary>Nulo quando concedido pela plataforma (primeiro Diretor).</summary>
    public Guid? GrantedBy { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>Obrigatório para SUPORTE_PLATAFORMA.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

/// <summary>Turmas concedidas ao perfil CONSULTA (D10). A FK para turmas entra com o módulo School (F2-04).</summary>
public sealed class ClassroomAccessGrant
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ClassroomId { get; set; }

    public Guid GrantedBy { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Sessão do cookie desta escola. Revogável; expira em no máximo 10 h.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset AuthTime { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }
}

public enum SupportGrantStatus
{
    Pending,
    Approved,
    Denied,
    Revoked,
    /// <summary>Retirado pelo suporte antes da decisão.</summary>
    Cancelled,
}

/// <summary>
/// Liberação de acesso de suporte nesta escola (P-11, D17). O id é o do pedido na plataforma.
/// Aprovada só pelo Diretor; vira o papel temporário SUPORTE_PLATAFORMA (somente leitura, até 24 h).
/// </summary>
public sealed class SupportAccessGrant
{
    public Guid Id { get; set; }

    /// <summary>Conta da plataforma que pediu (o "sub" do login único).</summary>
    public Guid UserId { get; set; }

    public required string RequesterName { get; set; }

    public required string RequesterEmail { get; set; }

    public required string Reason { get; set; }

    public int DurationHours { get; set; }

    public SupportGrantStatus Status { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Quando os diretores foram avisados por e-mail do pedido.</summary>
    public DateTimeOffset? NotifiedAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => Status == SupportGrantStatus.Approved && ExpiresAt > now;
}

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public const string Schema = "access";

    public DbSet<Member> Members => Set<Member>();

    public DbSet<UserRole> Roles => Set<UserRole>();

    public DbSet<ClassroomAccessGrant> ClassroomGrants => Set<ClassroomAccessGrant>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<SupportAccessGrant> SupportGrants => Set<SupportAccessGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Member>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.Property(x => x.Role).HasMaxLength(30);
            e.HasIndex(x => new { x.UserId, x.Role }).IsUnique().HasFilter("revoked_at IS NULL");
        });

        modelBuilder.Entity<ClassroomAccessGrant>(e =>
        {
            e.ToTable("classroom_access_grants");
            e.HasIndex(x => new { x.UserId, x.ClassroomId }).IsUnique().HasFilter("revoked_at IS NULL");
        });

        modelBuilder.Entity<SupportAccessGrant>(e =>
        {
            e.ToTable("support_access_grants");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RequesterName).HasMaxLength(200);
            e.Property(x => x.RequesterEmail).HasMaxLength(254);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("user_sessions");
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasIndex(x => x.UserId);
        });
    }
}

public sealed class AccessDbContextDesignFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccessDbContext>()
            .UseSineduPostgres("Host=localhost;Database=design", AccessDbContext.Schema)
            .Options);
}
