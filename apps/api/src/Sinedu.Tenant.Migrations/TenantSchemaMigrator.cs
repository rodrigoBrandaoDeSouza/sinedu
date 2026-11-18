using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.Migrations;

/// <summary>Versão de schema aplicada no banco da escola.</summary>
public sealed class SchemaInfo
{
    public int Id { get; set; }

    public required string Version { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}

public sealed class InfraDbContext(DbContextOptions<InfraDbContext> options) : DbContext(options)
{
    public const string Schema = "infra";

    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SchemaInfo>(e =>
        {
            e.ToTable("schema_info");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Version).HasMaxLength(30);
        });
    }
}

public sealed class InfraDbContextDesignFactory : IDesignTimeDbContextFactory<InfraDbContext>
{
    public InfraDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InfraDbContext>().UseSineduPostgres("Host=localhost;Database=design", InfraDbContext.Schema).Options);
}

/// <summary>
/// Aplica as migrations de todos os módulos da escola com o papel dono do banco e concede ao papel da
/// aplicação apenas DML. Auditoria: só INSERT/SELECT. Executado pela plataforma (provisionamento e atualização).
/// </summary>
public sealed class TenantSchemaMigrator : ITenantSchemaMigrator
{
    /// <summary>Schemas com CRUD completo para a aplicação.</summary>
    private static readonly string[] ReadWriteSchemas = [AccessDbContext.Schema];

    public async Task MigrateAsync(string ownerConnectionString, string appRole, CancellationToken cancellationToken = default)
    {
        await using (var audit = new AuditDbContext(Options<AuditDbContext>(ownerConnectionString, AuditDbContext.Schema)))
        {
            await audit.Database.MigrateAsync(cancellationToken);
        }

        await using (var access = new AccessDbContext(Options<AccessDbContext>(ownerConnectionString, AccessDbContext.Schema)))
        {
            await access.Database.MigrateAsync(cancellationToken);
        }

        await using var infra = new InfraDbContext(Options<InfraDbContext>(ownerConnectionString, InfraDbContext.Schema));
        await infra.Database.MigrateAsync(cancellationToken);

        await GrantAsync(ownerConnectionString, appRole, cancellationToken);

        var info = await infra.SchemaInfo.FirstOrDefaultAsync(cancellationToken);
        if (info is null)
        {
            infra.SchemaInfo.Add(new SchemaInfo { Id = 1, Version = TenantSchema.Version, AppliedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            info.Version = TenantSchema.Version;
            info.AppliedAt = DateTimeOffset.UtcNow;
        }

        await infra.SaveChangesAsync(cancellationToken);
    }

    public async Task AddInitialDirectorAsync(string ownerConnectionString, InitialDirector director, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(director);
        await using var db = new AccessDbContext(Options<AccessDbContext>(ownerConnectionString, AccessDbContext.Schema));

        // Salvaguarda §4: a plataforma só cria o PRIMEIRO Diretor.
        if (await db.Roles.AnyAsync(r => r.Role == Roles.Director && r.RevokedAt == null, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!await db.Members.AnyAsync(m => m.UserId == director.UserId, cancellationToken))
        {
            db.Members.Add(new Member
            {
                UserId = director.UserId,
                Email = director.Email,
                DisplayName = director.FullName,
                Status = MemberStatus.Active,
                CreatedAt = now,
            });
        }

        db.Roles.Add(new UserRole { Id = Guid.CreateVersion7(), UserId = director.UserId, Role = Roles.Director, GrantedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task GrantAsync(string ownerConnectionString, string appRole, CancellationToken cancellationToken)
    {
        var app = PersistenceExtensions.QuoteIdentifier(appRole);
        var statements = new List<string>();

        foreach (var schema in ReadWriteSchemas)
        {
            statements.Add($"GRANT USAGE ON SCHEMA {schema} TO {app}");
            statements.Add($"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {app}");
            statements.Add($"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {app}");
            statements.Add($"REVOKE ALL ON {schema}.__ef_migrations FROM {app}");
        }

        statements.Add($"GRANT USAGE ON SCHEMA {AuditDbContext.Schema} TO {app}");
        statements.Add($"REVOKE ALL ON ALL TABLES IN SCHEMA {AuditDbContext.Schema} FROM {app}");
        statements.Add($"GRANT SELECT, INSERT ON {AuditDbContext.Schema}.audit_events TO {app}");

        statements.Add($"GRANT USAGE ON SCHEMA {InfraDbContext.Schema} TO {app}");
        statements.Add($"GRANT SELECT ON {InfraDbContext.Schema}.schema_info TO {app}");

        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var sql in statements)
        {
#pragma warning disable CA2100 // Schemas são constantes; o papel vem de slug validado e é citado.
            await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static DbContextOptions<T> Options<T>(string connectionString, string schema)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSineduPostgres(connectionString, schema).Options;
}
