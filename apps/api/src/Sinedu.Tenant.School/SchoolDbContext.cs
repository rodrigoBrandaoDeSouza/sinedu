using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.School;

public enum SchoolYearStatus
{
    Planned,
    Active,
    Closed,
}

public enum Stage
{
    /// <summary>Ensino Fundamental — anos finais.</summary>
    Fundamental,

    /// <summary>Ensino Médio.</summary>
    Medio,

    Outra,
}

public enum Shift
{
    Manha,
    Tarde,
    Noite,
    Integral,
}

public enum ClassroomStatus
{
    Active,
    Closed,
}

public enum StaffFunction
{
    Diretor,
    Pedagogo,
    Professor,
    Secretaria,
    Outro,
}

/// <summary>Dados institucionais da escola (linha única) — usados em cabeçalhos e atas.</summary>
public sealed class SchoolProfile
{
    public int Id { get; set; } = 1;

    public required string Name { get; set; }

    public string? InstitutionalCode { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SchoolYear
{
    public Guid Id { get; set; }

    public int Year { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public SchoolYearStatus Status { get; set; }

    public List<AcademicTerm> Terms { get; set; } = [];
}

public sealed class AcademicTerm
{
    public Guid Id { get; set; }

    public Guid SchoolYearId { get; set; }

    public int Number { get; set; }

    public required string Name { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }
}

/// <summary>Série/etapa (ex.: 6º ano, 1ª série do EM).</summary>
public sealed class Grade
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Forma curta usada no nome da turma (ex.: "6º", "1ª").</summary>
    public required string ShortName { get; set; }

    public Stage Stage { get; set; }

    public int SortOrder { get; set; }
}

public sealed class Classroom
{
    public Guid Id { get; set; }

    public Guid SchoolYearId { get; set; }

    public SchoolYear? SchoolYear { get; set; }

    public Guid GradeId { get; set; }

    public Grade? Grade { get; set; }

    /// <summary>Identificação dentro da série (ex.: "A").</summary>
    public required string Section { get; set; }

    public Shift Shift { get; set; }

    public int? Capacity { get; set; }

    public ClassroomStatus Status { get; set; }

    /// <summary>Código da turma no sistema de origem (ex.: SERE).</summary>
    public string? ExternalCode { get; set; }

    public List<TeacherAssignment> Teachers { get; set; } = [];

    public static string DisplayName(string gradeShort, string section, Shift shift) =>
        $"{gradeShort} {section} {ShiftLabel(shift)}";

    public static string ShiftLabel(Shift shift) => shift switch
    {
        Shift.Manha => "Manhã",
        Shift.Tarde => "Tarde",
        Shift.Noite => "Noite",
        _ => "Integral",
    };
}

/// <summary>Profissional da escola. Pode existir sem conta (vindo de importação); <see cref="UserId"/> liga à conta de acesso.</summary>
public sealed class StaffMember
{
    public Guid Id { get; set; }

    public required string FullName { get; set; }

    public Guid? UserId { get; set; }

    public StaffFunction Function { get; set; }

    public string? ProfessionalEmail { get; set; }

    public string? ProfessionalPhone { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TeacherAssignment
{
    public Guid Id { get; set; }

    public Guid StaffMemberId { get; set; }

    public StaffMember? StaffMember { get; set; }

    public Guid ClassroomId { get; set; }

    public string? Subject { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }
}

public sealed class SchoolDbContext(DbContextOptions<SchoolDbContext> options) : DbContext(options)
{
    public const string Schema = "school";

    public DbSet<SchoolProfile> Profile => Set<SchoolProfile>();

    public DbSet<SchoolYear> Years => Set<SchoolYear>();

    public DbSet<AcademicTerm> Terms => Set<AcademicTerm>();

    public DbSet<Grade> Grades => Set<Grade>();

    public DbSet<Classroom> Classrooms => Set<Classroom>();

    public DbSet<StaffMember> Staff => Set<StaffMember>();

    public DbSet<TeacherAssignment> TeacherAssignments => Set<TeacherAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SchoolProfile>(e =>
        {
            e.ToTable("school_profile");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.InstitutionalCode).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(254);
        });

        modelBuilder.Entity<SchoolYear>(e =>
        {
            e.ToTable("school_years");
            e.HasIndex(x => x.Year).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Terms).WithOne().HasForeignKey(x => x.SchoolYearId);
        });

        modelBuilder.Entity<AcademicTerm>(e =>
        {
            e.ToTable("academic_terms");
            e.Property(x => x.Name).HasMaxLength(60);
            e.HasIndex(x => new { x.SchoolYearId, x.Number }).IsUnique();
        });

        modelBuilder.Entity<Grade>(e =>
        {
            e.ToTable("grades");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.ShortName).HasMaxLength(10);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Stage, x.ShortName }).IsUnique();
        });

        modelBuilder.Entity<Classroom>(e =>
        {
            e.ToTable("classrooms");
            e.Property(x => x.Section).HasMaxLength(10);
            e.Property(x => x.Shift).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExternalCode).HasMaxLength(30);
            e.HasIndex(x => new { x.SchoolYearId, x.GradeId, x.Section, x.Shift }).IsUnique();
            e.HasOne(x => x.SchoolYear).WithMany().HasForeignKey(x => x.SchoolYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Teachers).WithOne().HasForeignKey(x => x.ClassroomId);
        });

        modelBuilder.Entity<StaffMember>(e =>
        {
            e.ToTable("staff_members");
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Function).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProfessionalEmail).HasMaxLength(254);
            e.Property(x => x.ProfessionalPhone).HasMaxLength(30);
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
        });

        modelBuilder.Entity<TeacherAssignment>(e =>
        {
            e.ToTable("teacher_assignments");
            e.Property(x => x.Subject).HasMaxLength(80);
            e.HasOne(x => x.StaffMember).WithMany().HasForeignKey(x => x.StaffMemberId);
            e.HasIndex(x => new { x.StaffMemberId, x.ClassroomId });
        });
    }
}

public sealed class SchoolDbContextDesignFactory : IDesignTimeDbContextFactory<SchoolDbContext>
{
    public SchoolDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres("Host=localhost;Database=design", SchoolDbContext.Schema).Options);
}
