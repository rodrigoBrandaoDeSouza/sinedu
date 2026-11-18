using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.Imports;

public enum ImportStatus
{
    /// <summary>Arquivo lido; aguardando mapeamento.</summary>
    Uploaded,

    /// <summary>Mapeado e validado; aguardando revisão e confirmação.</summary>
    Validated,

    Processing,
    Completed,
    Cancelled,
    Failed,
}

public enum RowAction
{
    Pending,
    Empty,
    New,
    Update,
    Unchanged,
    PossibleDuplicate,
    Conflict,
    Rejected,
}

public enum RowDecision
{
    None,
    Create,
    Update,
    Skip,
}

public enum RowOutcome
{
    None,
    Created,
    Updated,
    Unchanged,
    Skipped,
    Failed,
}

/// <summary>
/// Lote de importação. O arquivo original NÃO é guardado — só o hash, os cabeçalhos e as linhas,
/// que têm o conteúdo apagado em <see cref="PurgeAfter"/> (P5).
/// </summary>
public sealed class ImportBatch
{
    public Guid Id { get; set; }

    public required string FileName { get; set; }

    public required string FileSha256 { get; set; }

    public long FileSize { get; set; }

    public ImportStatus Status { get; set; }

    /// <summary>Abas e cabeçalhos detectados (JSON).</summary>
    public required string SourceJson { get; set; }

    public string? Sheet { get; set; }

    public int? HeaderRow { get; set; }

    /// <summary>Campo → índice da coluna (JSON).</summary>
    public string? MappingJson { get; set; }

    /// <summary>Totais da prévia e do resultado (JSON).</summary>
    public string? TotalsJson { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ValidatedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset PurgeAfter { get; set; }
}

public sealed class ImportRow
{
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }

    public required string Sheet { get; set; }

    /// <summary>Número da linha na planilha (1 = primeira linha).</summary>
    public int RowNumber { get; set; }

    /// <summary>Células como texto. Apagadas após o prazo de retenção.</summary>
    public string?[] Cells { get; set; } = [];

    public RowAction Action { get; set; }

    public RowDecision Decision { get; set; }

    public RowOutcome Outcome { get; set; }

    public Guid? TargetStudentId { get; set; }

    public Guid[] Candidates { get; set; } = [];

    /// <summary>Campos que mudariam (só nomes de campo, nunca valores).</summary>
    public string[] Changes { get; set; } = [];

    /// <summary>Mensagens de erro/aviso. Nunca contêm o valor da célula.</summary>
    public string[] Errors { get; set; } = [];

    public string[] Warnings { get; set; } = [];

    public Guid? ResolvedBy { get; set; }
}

public sealed class ImportsDbContext(DbContextOptions<ImportsDbContext> options) : DbContext(options)
{
    public const string Schema = "imports";

    public DbSet<ImportBatch> Batches => Set<ImportBatch>();

    public DbSet<ImportRow> Rows => Set<ImportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.ToTable("batches");
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.FileSha256).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceJson).HasColumnType("jsonb");
            e.Property(x => x.Sheet).HasMaxLength(100);
            e.Property(x => x.MappingJson).HasColumnType("jsonb");
            e.Property(x => x.TotalsJson).HasColumnType("jsonb");
            e.Property<uint>("xmin").IsRowVersion();
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<ImportRow>(e =>
        {
            e.ToTable("rows");
            e.Property(x => x.Sheet).HasMaxLength(100);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.BatchId, x.Sheet, x.RowNumber });
            e.HasIndex(x => new { x.BatchId, x.Action });
        });
    }
}

public sealed class ImportsDbContextDesignFactory : IDesignTimeDbContextFactory<ImportsDbContext>
{
    public ImportsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ImportsDbContext>().UseSineduPostgres("Host=localhost;Database=design", ImportsDbContext.Schema).Options);
}
