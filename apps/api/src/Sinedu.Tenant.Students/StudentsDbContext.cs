using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.Students;

public enum StudentStatus
{
    Active,
    Inactive,
}

public enum EnrollmentStatus
{
    Ativa,
    Transferida,
    Remanejada,
    Concluida,
    Cancelada,

    /// <summary>Rematrícula para o próximo ano letivo (ainda não começou). Não conta como matrícula ativa.</summary>
    Prevista,
}

public enum DataSource
{
    Manual,
    Import,
}

public enum GuardianRelationship
{
    Mae,
    Pai,
    Avos,
    Responsavel,
    ProprioAluno,
    Outro,
}

public sealed class Student
{
    public Guid Id { get; set; }

    public required string FullName { get; set; }

    public string? SocialName { get; set; }

    /// <summary>Nome em maiúsculas e sem acentos, para busca.</summary>
    public required string SearchName { get; set; }

    public DateOnly? BirthDate { get; set; }

    /// <summary>Código institucional (CGM no Paraná). Identificador confiável para importação.</summary>
    public string? InstitutionalCode { get; set; }

    public string? InstitutionalEmail { get; set; }

    public StudentStatus Status { get; set; }

    public DataSource Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Enrollment> Enrollments { get; set; } = [];

    public List<StudentAddress> Addresses { get; set; } = [];

    public List<StudentGuardian> Guardians { get; set; } = [];

    public static string ToSearch(string value) =>
        new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant().Trim();
}

/// <summary>Matrícula: histórico preservado — mudar de turma encerra a atual e abre outra.</summary>
public sealed class Enrollment
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public Guid ClassroomId { get; set; }

    public Guid SchoolYearId { get; set; }

    public EnrollmentStatus Status { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    public int? CallNumber { get; set; }
}

/// <summary>Endereço com vigência (o anterior é encerrado, não sobrescrito).</summary>
public sealed class StudentAddress
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public required string Line { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public DataSource Source { get; set; }
}

public sealed class Guardian
{
    public Guid Id { get; set; }

    public required string FullName { get; set; }

    /// <summary>CPF ou RG, quando informado pela fonte.</summary>
    public string? Document { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DataSource Source { get; set; }
}

public sealed class StudentGuardian
{
    public Guid StudentId { get; set; }

    public Guid GuardianId { get; set; }

    public Guardian? Guardian { get; set; }

    public GuardianRelationship Relationship { get; set; }

    public bool IsPrimaryContact { get; set; }

    public bool ContactAuthorized { get; set; }

    /// <summary>Fundamento da autorização de contato (spec 6.1): ex. "termo assinado na matrícula". Nunca presumida.</summary>
    public string? ContactBasis { get; set; }

    public DateTimeOffset? ContactAuthorizedAt { get; set; }

    public Guid? ContactAuthorizedBy { get; set; }
}

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options) : DbContext(options)
{
    public const string Schema = "students";

    public DbSet<Student> Students => Set<Student>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    public DbSet<StudentAddress> Addresses => Set<StudentAddress>();

    public DbSet<Guardian> Guardians => Set<Guardian>();

    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Student>(e =>
        {
            e.ToTable("students");
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.SocialName).HasMaxLength(200);
            e.Property(x => x.SearchName).HasMaxLength(200);
            e.Property(x => x.InstitutionalCode).HasMaxLength(20);
            e.Property(x => x.InstitutionalEmail).HasMaxLength(254);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasIndex(x => x.InstitutionalCode).IsUnique().HasFilter("institutional_code IS NOT NULL");
            e.HasIndex(x => x.SearchName).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasMany(x => x.Enrollments).WithOne().HasForeignKey(x => x.StudentId);
            e.HasMany(x => x.Addresses).WithOne().HasForeignKey(x => x.StudentId);
            e.HasMany(x => x.Guardians).WithOne().HasForeignKey(x => x.StudentId);
        });

        modelBuilder.Entity<Enrollment>(e =>
        {
            e.ToTable("enrollments");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.ClassroomId, x.Status });
            e.HasIndex(x => new { x.StudentId, x.StartsOn });
            // Uma única matrícula ativa por aluno.
            e.HasIndex(x => x.StudentId).IsUnique().HasFilter("status = 'Ativa'").HasDatabaseName("ux_enrollments_one_active");
        });

        modelBuilder.Entity<StudentAddress>(e =>
        {
            e.ToTable("addresses");
            e.Property(x => x.Line).HasMaxLength(300);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Guardian>(e =>
        {
            e.ToTable("guardians");
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Document).HasMaxLength(30);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Document);
        });

        modelBuilder.Entity<StudentGuardian>(e =>
        {
            e.ToTable("student_guardians");
            e.HasKey(x => new { x.StudentId, x.GuardianId });
            e.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ContactBasis).HasMaxLength(300);
            e.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => x.GuardianId);
        });
    }
}

public sealed class StudentsDbContextDesignFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<StudentsDbContext>().UseSineduPostgres("Host=localhost;Database=design", StudentsDbContext.Schema).Options);
}
