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

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public const string Schema = "access";

    public DbSet<Member> Members => Set<Member>();

    public DbSet<UserRole> Roles => Set<UserRole>();

    public DbSet<ClassroomAccessGrant> ClassroomGrants => Set<ClassroomAccessGrant>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

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
