using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Tenants;

public sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options) : DbContext(options)
{
    public const string Schema = "tenants";

    public DbSet<Network> Networks => Set<Network>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<ProvisioningStep> ProvisioningSteps => Set<ProvisioningStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Network>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Uf).HasMaxLength(2);
            e.Property(x => x.Municipality).HasMaxLength(120);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(63);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.InstitutionalCode).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.StatusReason).HasMaxLength(500);
            e.Property(x => x.DatabaseName).HasMaxLength(80);
            e.Property(x => x.SecretPath).HasMaxLength(120);
            e.Property(x => x.TransitKeyName).HasMaxLength(80);
            e.Property(x => x.BucketName).HasMaxLength(80);
            e.Property(x => x.SchemaVersion).HasMaxLength(30);
            e.Property(x => x.FirstDirectorEmail).HasMaxLength(254);
            e.Property(x => x.FirstDirectorName).HasMaxLength(200);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasOne(x => x.Network).WithMany().HasForeignKey(x => x.NetworkId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.ProvisioningSteps).WithOne().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<ProvisioningStep>(e =>
        {
            e.Property(x => x.Step).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.Step });
        });
    }
}

public sealed class TenantsDbContextDesignFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenantsDbContext>()
            .UseSineduPostgres("Host=localhost;Database=sinedu_platform", TenantsDbContext.Schema)
            .Options);
}

/// <summary>Leitura do registro pela própria plataforma (ex.: validar redirect_uri do OIDC).</summary>
internal sealed class PlatformTenantDirectory(TenantsDbContext db) : ITenantDirectory
{
    public async Task<TenantRegistration?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking()
            .Where(t => t.Slug == slug)
            .Select(t => new TenantRegistration(
                t.Id, t.Slug, t.DisplayName, t.Status, t.SchemaVersion, t.DatabaseName, t.SecretPath, t.TransitKeyName, t.BucketName))
            .FirstOrDefaultAsync(cancellationToken);
}
