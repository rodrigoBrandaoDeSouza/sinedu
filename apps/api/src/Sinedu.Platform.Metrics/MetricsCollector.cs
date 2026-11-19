using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Npgsql;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Tenancy;
using TenantStatus = Sinedu.SharedKernel.Tenancy.TenantStatus;

namespace Sinedu.Platform.Metrics;

/// <summary>Foto das contagens de uma escola num momento. Só números — nada de aluno, nada de Saúde/Proteção.</summary>
public sealed class TenantSnapshot
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public DateTimeOffset CollectedAt { get; set; }

    public int ActiveStudents { get; set; }

    public int ActiveClassrooms { get; set; }

    public int ActiveMembers { get; set; }

    public int OccurrencesLast30Days { get; set; }

    public int EmailsSentThisMonth { get; set; }

    public int EmailsFailedOpen { get; set; }

    public long CommonStorageBytes { get; set; }

    public string? SchemaVersion { get; set; }

    /// <summary>Falha da coleta (sanitizada). Quando preenchido, os números são zero.</summary>
    public string? Error { get; set; }
}

public sealed class MetricsDbContext(DbContextOptions<MetricsDbContext> options) : DbContext(options)
{
    public const string Schema = "metrics";

    public DbSet<TenantSnapshot> Snapshots => Set<TenantSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<TenantSnapshot>(e =>
        {
            e.ToTable("tenant_snapshots");
            e.Property(x => x.SchemaVersion).HasMaxLength(40);
            e.Property(x => x.Error).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CollectedAt });
        });
    }
}

public sealed class MetricsDbContextDesignFactory : IDesignTimeDbContextFactory<MetricsDbContext>
{
    public MetricsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MetricsDbContext>().UseSineduPostgres("Host=localhost;Database=design", MetricsDbContext.Schema).Options);
}

/// <summary>
/// Coleta P-10. Conecta em cada banco de escola com o papel <c>sinedu_metrics</c>, que só pode ler
/// <c>metrics.v_counts</c> (o migrator da escola concede isso e nada mais). Senha no Vault, nunca em configuração.
/// </summary>
public sealed class MetricsCollector(MetricsDbContext db, TenantsDbContext tenants, ISecretStore secrets, IOptions<TenantDatabaseOptions> options, TimeProvider clock)
{
    /// <summary>Garante o papel de métricas no servidor (idempotente). Chamado no `migrate` da plataforma.</summary>
    public static async Task EnsureRoleAsync(ISecretStore secrets, string adminConnectionString, CancellationToken ct)
    {
        var stored = await secrets.ReadAsync(MetricsContract.SecretPath, ct);
        var password = stored?.GetValueOrDefault("password");
        if (string.IsNullOrEmpty(password))
        {
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', 'a').Replace('/', 'b');
            await secrets.WriteAsync(MetricsContract.SecretPath, new Dictionary<string, string> { ["password"] = password }, ct);
        }

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(ct);
        await using var exists = new NpgsqlCommand($"SELECT 1 FROM pg_roles WHERE rolname = '{MetricsContract.Role}'", connection);
        var verb = await exists.ExecuteScalarAsync(ct) is null ? "CREATE" : "ALTER";
        // Senha gerada aqui (só letras/números): segura para interpolar. NOINHERIT e sem nenhum outro privilégio.
#pragma warning disable CA2100
        await using var command = new NpgsqlCommand($"{verb} ROLE {MetricsContract.Role} LOGIN NOINHERIT NOCREATEDB NOCREATEROLE PASSWORD '{password}'", connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CollectAsync(CancellationToken ct)
    {
        var password = (await secrets.ReadAsync(MetricsContract.SecretPath, ct))?.GetValueOrDefault("password")
            ?? throw new InvalidOperationException("Papel de métricas não configurado. Rode `migrate` na plataforma.");
        var admin = new NpgsqlConnectionStringBuilder(options.Value.AdminConnectionString);
        var active = await tenants.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended)
            .Select(t => new { t.Id, t.DatabaseName })
            .ToListAsync(ct);
        var now = clock.GetUtcNow();

        foreach (var tenant in active)
        {
            var snapshot = new TenantSnapshot { Id = Guid.CreateVersion7(), TenantId = tenant.Id, CollectedAt = now };
            try
            {
                var cs = new NpgsqlConnectionStringBuilder
                {
                    Host = admin.Host, Port = admin.Port, Database = tenant.DatabaseName, Username = MetricsContract.Role, Password = password,
                    Pooling = false, Timeout = 10, CommandTimeout = 30,
                };
                await using var connection = new NpgsqlConnection(cs.ConnectionString);
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand("SELECT * FROM metrics.v_counts", connection);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    snapshot.ActiveStudents = Convert.ToInt32(reader["active_students"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.ActiveClassrooms = Convert.ToInt32(reader["active_classrooms"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.ActiveMembers = Convert.ToInt32(reader["active_members"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.OccurrencesLast30Days = Convert.ToInt32(reader["occurrences_last_30_days"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.EmailsSentThisMonth = Convert.ToInt32(reader["emails_sent_this_month"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.EmailsFailedOpen = Convert.ToInt32(reader["emails_failed_open"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.CommonStorageBytes = Convert.ToInt64(reader["common_storage_bytes"], System.Globalization.CultureInfo.InvariantCulture);
                    snapshot.SchemaVersion = reader["schema_version"] as string;
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
            {
                snapshot.Error = ex is PostgresException pg ? $"Banco recusou ({pg.SqlState})." : "Banco da escola indisponível.";
            }

            db.Snapshots.Add(snapshot);
        }

        await db.SaveChangesAsync(ct);
        return active.Count;
    }
}

/// <summary>Última contagem de alunos ativos por escola (só o número). Contrato público usado pela cobrança (P-08).</summary>
public sealed record StudentCount(int ActiveStudents, DateTimeOffset CollectedAt);

public sealed class StudentCounts(MetricsDbContext db)
{
    public async Task<Dictionary<Guid, StudentCount>> LatestAsync(CancellationToken ct)
    {
        var rows = await db.Snapshots.AsNoTracking()
            .Where(s => s.Error == null)
            .Where(s => db.Snapshots.Where(x => x.TenantId == s.TenantId && x.Error == null).Max(x => x.CollectedAt) == s.CollectedAt)
            .Select(s => new { s.TenantId, s.ActiveStudents, s.CollectedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TenantId).ToDictionary(g => g.Key, g => new StudentCount(g.First().ActiveStudents, g.First().CollectedAt));
    }
}
