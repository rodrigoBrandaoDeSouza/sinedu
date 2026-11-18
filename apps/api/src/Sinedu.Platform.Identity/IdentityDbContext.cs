using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Platform.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<PlatformRoleGrant> PlatformRoles => Set<PlatformRoleGrant>();

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<AccountToken> Tokens => Set<AccountToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.UseOpenIddict();

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotpSecretEncrypted).HasMaxLength(500);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasMany(x => x.PlatformRoles).WithOne().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.Memberships).WithOne().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<PlatformRoleGrant>(e =>
        {
            e.ToTable("platform_roles");
            e.Property(x => x.Role).HasMaxLength(30);
            e.HasIndex(x => new { x.UserId, x.Role }).IsUnique().HasFilter("revoked_at IS NULL");
        });

        modelBuilder.Entity<TenantMembership>(e =>
        {
            e.ToTable("tenant_memberships");
            e.Property(x => x.TenantSlug).HasMaxLength(63);
            e.Property(x => x.TenantName).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
        });

        modelBuilder.Entity<RecoveryCode>(e =>
        {
            e.ToTable("recovery_codes");
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<AccountToken>(e =>
        {
            e.ToTable("account_tokens");
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });
    }
}

public sealed class IdentityDbContextDesignFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSineduPostgres("Host=localhost;Database=sinedu_platform", IdentityDbContext.Schema)
            .Options);
}
