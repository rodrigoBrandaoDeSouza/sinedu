using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.Occurrences;

/// <summary>Ciclo de vida (spec §5). Registrada nunca é excluída: só cancelada com justificativa (P4).</summary>
public enum OccurrenceStatus
{
    Rascunho,
    Registrada,
    EmAcompanhamento,
    Concluida,
    Cancelada,
}

public sealed class OccurrenceCategory
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

public sealed class OccurrenceSeverity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// Registro institucional de um fato e das providências — não é diagnóstico nem rótulo do aluno.
/// Matrícula, turma e ano gravados são os da DATA DO FATO, mesmo que o aluno mude de turma depois.
/// </summary>
public sealed class Occurrence
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid ClassroomId { get; set; }

    public Guid SchoolYearId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid CategoryId { get; set; }

    public Guid? SeverityId { get; set; }

    public string? Location { get; set; }

    public required string Description { get; set; }

    public string? Involved { get; set; }

    public string? ActionsTaken { get; set; }

    public string? Referrals { get; set; }

    public string? Notes { get; set; }

    public OccurrenceStatus Status { get; set; }

    public Guid CreatedBy { get; set; }

    public required string CreatedByName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RegisteredAt { get; set; }

    public Guid? RegisteredBy { get; set; }

    public string? RegisteredByName { get; set; }

    public DateTimeOffset? ConcludedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    /// <summary>0 enquanto rascunho; cada mudança depois de registrada gera uma revisão e incrementa.</summary>
    public int Version { get; set; }
}

/// <summary>Revisão imutável: estado completo depois da mudança, quem fez, quando e por quê.</summary>
public sealed class OccurrenceRevision
{
    public Guid Id { get; set; }

    public Guid OccurrenceId { get; set; }

    public int Version { get; set; }

    public required string Action { get; set; }

    public required string Snapshot { get; set; }

    public required string Reason { get; set; }

    public Guid ChangedBy { get; set; }

    public required string ChangedByName { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class OccurrenceAttachment
{
    public Guid Id { get; set; }

    public Guid OccurrenceId { get; set; }

    public required string ObjectKey { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256 { get; set; }

    public Guid UploadedBy { get; set; }

    public required string UploadedByName { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}

public sealed class OccurrencesDbContext(DbContextOptions<OccurrencesDbContext> options) : DbContext(options)
{
    public const string Schema = "occurrences";

    /// <summary>Categorias iniciais da spec (configuráveis pela Direção depois).</summary>
    public static readonly (Guid Id, string Name)[] DefaultCategories =
    [
        (new("0193a000-0000-7000-8000-000000000001"), "Indisciplina"),
        (new("0193a000-0000-7000-8000-000000000002"), "Conflito"),
        (new("0193a000-0000-7000-8000-000000000003"), "Atraso recorrente"),
        (new("0193a000-0000-7000-8000-000000000004"), "Desrespeito"),
        (new("0193a000-0000-7000-8000-000000000005"), "Dano material"),
        (new("0193a000-0000-7000-8000-000000000006"), "Outros"),
    ];

    public static readonly (Guid Id, string Name)[] DefaultSeverities =
    [
        (new("0193a000-0000-7000-8000-000000000101"), "Leve"),
        (new("0193a000-0000-7000-8000-000000000102"), "Moderada"),
        (new("0193a000-0000-7000-8000-000000000103"), "Grave"),
    ];

    public DbSet<OccurrenceCategory> Categories => Set<OccurrenceCategory>();

    public DbSet<OccurrenceSeverity> Severities => Set<OccurrenceSeverity>();

    public DbSet<Occurrence> Occurrences => Set<Occurrence>();

    public DbSet<OccurrenceRevision> Revisions => Set<OccurrenceRevision>();

    public DbSet<OccurrenceAttachment> Attachments => Set<OccurrenceAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<OccurrenceCategory>(e =>
        {
            e.ToTable("categories");
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasData(DefaultCategories.Select((c, i) => new OccurrenceCategory { Id = c.Id, Name = c.Name, IsActive = true, SortOrder = i + 1 }));
        });

        modelBuilder.Entity<OccurrenceSeverity>(e =>
        {
            e.ToTable("severities");
            e.Property(x => x.Name).HasMaxLength(40);
            e.HasData(DefaultSeverities.Select((s, i) => new OccurrenceSeverity { Id = s.Id, Name = s.Name, SortOrder = i + 1 }));
        });

        modelBuilder.Entity<Occurrence>(e =>
        {
            e.ToTable("occurrences");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.Involved).HasMaxLength(2000);
            e.Property(x => x.ActionsTaken).HasMaxLength(4000);
            e.Property(x => x.Referrals).HasMaxLength(4000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.CancelReason).HasMaxLength(1000);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.Property(x => x.RegisteredByName).HasMaxLength(200);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasOne<OccurrenceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OccurrenceSeverity>().WithMany().HasForeignKey(x => x.SeverityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.ClassroomId, x.OccurredAt });
            e.HasIndex(x => new { x.StudentId, x.OccurredAt });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<OccurrenceRevision>(e =>
        {
            e.ToTable("revisions");
            e.Property(x => x.Action).HasMaxLength(30);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.ChangedByName).HasMaxLength(200);
            e.HasIndex(x => new { x.OccurrenceId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<OccurrenceAttachment>(e =>
        {
            e.ToTable("attachments");
            e.Property(x => x.ObjectKey).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(60);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.UploadedByName).HasMaxLength(200);
            e.HasIndex(x => x.OccurrenceId);
        });
    }
}

public sealed class OccurrencesDbContextDesignFactory : IDesignTimeDbContextFactory<OccurrencesDbContext>
{
    public OccurrencesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OccurrencesDbContext>().UseSineduPostgres("Host=localhost;Database=design", OccurrencesDbContext.Schema).Options);
}
