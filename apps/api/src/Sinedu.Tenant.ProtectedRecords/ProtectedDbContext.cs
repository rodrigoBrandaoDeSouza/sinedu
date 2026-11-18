using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.ProtectedRecords;

public enum RecordKind
{
    Health,
    Protection,
}

/// <summary>Tipo de registro de Proteção/Sigilo. Fica em claro (P3) para listar vigências sem decifrar.</summary>
public enum ProtectionKind
{
    Guarda,
    Tutela,
    MedidaProtetiva,
    RestricaoJudicial,
    AutorizacaoRetirada,
    ProtecaoCriancaAdolescente,
    Outro,
}

public enum ProtectionStatus
{
    Vigente,
    Revogada,
}

/// <summary>Ficha de Saúde do aluno (uma por aluno). Conteúdo só nas versões cifradas.</summary>
public sealed class HealthRecord
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public int CurrentVersion { get; set; }

    /// <summary>Data de revisão combinada (em claro, para a lista de pendências).</summary>
    public DateOnly? ReviewDueOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Registro de Proteção/Sigilo. Em claro só tipo, situação e validade (P3).</summary>
public sealed class ProtectionRecord
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public ProtectionKind Kind { get; set; }

    public ProtectionStatus Status { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public int CurrentVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Versão imutável de um registro protegido. O conteúdo (dados + justificativa da alteração) é um envelope
/// AES-256-GCM com DEK própria, cifrada pela chave da escola no Vault. AAD amarra escola, aluno, tipo, registro e versão.
/// </summary>
public sealed class RecordVersion
{
    public Guid Id { get; set; }

    public RecordKind RecordKind { get; set; }

    public Guid RecordId { get; set; }

    public Guid StudentId { get; set; }

    public int Version { get; set; }

    public int PayloadSchemaVersion { get; set; }

    public required byte[] Ciphertext { get; set; }

    public required byte[] Nonce { get; set; }

    public required byte[] Tag { get; set; }

    public required string WrappedKey { get; set; }

    public Guid CreatedBy { get; set; }

    /// <summary>Nome de quem alterou (dado de equipe, não sensível), para o histórico sem consultar outro módulo.</summary>
    public required string CreatedByName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Anexo protegido: arquivo cifrado na API antes de ir ao S3; nome e tipo ficam no metadado cifrado.</summary>
public sealed class SecureAttachment
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public RecordKind OwnerKind { get; set; }

    public Guid OwnerId { get; set; }

    public required string ObjectKey { get; set; }

    public required byte[] Nonce { get; set; }

    public required byte[] Tag { get; set; }

    public required string WrappedKey { get; set; }

    public required byte[] MetadataCiphertext { get; set; }

    public required byte[] MetadataNonce { get; set; }

    public required byte[] MetadataTag { get; set; }

    public required string MetadataWrappedKey { get; set; }

    public long SizeBytes { get; set; }

    public required string CiphertextSha256 { get; set; }

    public Guid CreatedBy { get; set; }

    public required string CreatedByName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Retirado da lista com justificativa. O objeto cifrado é mantido (nada some silenciosamente).</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    public Guid? RemovedBy { get; set; }
}

/// <summary>Exportação excepcional: quem, quando, o quê. A justificativa fica cifrada.</summary>
public sealed class ProtectedExport
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public required string Sections { get; set; }

    public required byte[] JustificationCiphertext { get; set; }

    public required byte[] JustificationNonce { get; set; }

    public required byte[] JustificationTag { get; set; }

    public required string JustificationWrappedKey { get; set; }

    public Guid CreatedBy { get; set; }

    public required string CreatedByName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Schema <c>protected</c>: só este módulo acessa. Nenhum outro módulo referencia estas tabelas.</summary>
public sealed class ProtectedDbContext(DbContextOptions<ProtectedDbContext> options) : DbContext(options)
{
    public const string Schema = "protected";

    public DbSet<HealthRecord> HealthRecords => Set<HealthRecord>();

    public DbSet<ProtectionRecord> ProtectionRecords => Set<ProtectionRecord>();

    public DbSet<RecordVersion> Versions => Set<RecordVersion>();

    public DbSet<SecureAttachment> Attachments => Set<SecureAttachment>();

    public DbSet<ProtectedExport> Exports => Set<ProtectedExport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<HealthRecord>(e =>
        {
            e.ToTable("health_records");
            e.HasIndex(x => x.StudentId).IsUnique();
            e.HasIndex(x => x.ReviewDueOn);
            e.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<ProtectionRecord>(e =>
        {
            e.ToTable("protection_records");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => new { x.Status, x.ValidUntil });
            e.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<RecordVersion>(e =>
        {
            e.ToTable("record_versions");
            e.Property(x => x.RecordKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.WrappedKey).HasMaxLength(500);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasIndex(x => new { x.RecordKind, x.RecordId, x.Version }).IsUnique();
            e.HasIndex(x => x.StudentId);
        });

        modelBuilder.Entity<SecureAttachment>(e =>
        {
            e.ToTable("secure_attachments");
            e.Property(x => x.OwnerKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ObjectKey).HasMaxLength(200);
            e.Property(x => x.WrappedKey).HasMaxLength(500);
            e.Property(x => x.MetadataWrappedKey).HasMaxLength(500);
            e.Property(x => x.CiphertextSha256).HasMaxLength(64);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasIndex(x => new { x.StudentId, x.OwnerKind, x.OwnerId });
        });

        modelBuilder.Entity<ProtectedExport>(e =>
        {
            e.ToTable("exports");
            e.Property(x => x.Sections).HasMaxLength(100);
            e.Property(x => x.JustificationWrappedKey).HasMaxLength(500);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasIndex(x => x.StudentId);
        });
    }
}

public sealed class ProtectedDbContextDesignFactory : IDesignTimeDbContextFactory<ProtectedDbContext>
{
    public ProtectedDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProtectedDbContext>().UseSineduPostgres("Host=localhost;Database=design", ProtectedDbContext.Schema).Options);
}
