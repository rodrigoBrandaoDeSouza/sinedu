using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Platform.Billing;

/// <summary>Plano com valor mensal fixo por faixa de alunos ativos (P-06).</summary>
public sealed class Plan
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public List<PlanTier> Tiers { get; set; } = [];

    /// <summary>Faixa que cobre a quantidade de alunos. Nula quando passa da última faixa limitada (alerta de limite).</summary>
    public PlanTier? TierFor(int students) =>
        Tiers.OrderBy(t => t.UpToStudents ?? int.MaxValue).FirstOrDefault(t => t.UpToStudents is null || students <= t.UpToStudents);
}

public sealed class PlanTier
{
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }

    /// <summary>Até quantos alunos (inclusive). Nulo = "acima disso", sem limite.</summary>
    public int? UpToStudents { get; set; }

    public decimal MonthlyPrice { get; set; }

    public string Label(int? previousUpTo) => UpToStudents is { } up
        ? $"{(previousUpTo ?? 0) + 1} a {up} alunos"
        : $"Acima de {previousUpTo ?? 0} alunos";
}

public enum ContractStatus
{
    Active,
    Ended,
    Cancelled,
}

/// <summary>
/// Contrato de uma escola (P-07). Pagante é a APMF da escola (D16, decisão do produto): nome, CNPJ e contato.
/// Gera uma fatura por mês de vigência.
/// </summary>
public sealed class Contract
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PlanId { get; set; }

    public Plan? Plan { get; set; }

    public required string PayerName { get; set; }

    /// <summary>CNPJ da APMF (só dígitos).</summary>
    public string? PayerDocument { get; set; }

    public string? PayerContactName { get; set; }

    public string? PayerEmail { get; set; }

    public string? PayerPhone { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    /// <summary>Dia do vencimento no mês da fatura (1 a 28).</summary>
    public int DueDay { get; set; } = 10;

    public ContractStatus Status { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool Covers(DateOnly month) =>
        Status == ContractStatus.Active && StartsOn <= month.AddMonths(1).AddDays(-1) && (EndsOn is null || EndsOn >= month);
}

public enum InvoiceStatus
{
    Open,
    Paid,
    Cancelled,
}

/// <summary>Fatura mensal (P-08). Guarda a contagem e a faixa usadas no cálculo. Baixa manual; NF opcional.</summary>
public sealed class Invoice
{
    public Guid Id { get; set; }

    public Guid ContractId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Primeiro dia do mês de referência.</summary>
    public DateOnly ReferenceMonth { get; set; }

    public int Students { get; set; }

    public required string TierLabel { get; set; }

    public decimal Amount { get; set; }

    public DateOnly DueOn { get; set; }

    public InvoiceStatus Status { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public DateOnly? PaidOn { get; set; }

    public decimal? PaidAmount { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsOverdue(DateOnly today) => Status == InvoiceStatus.Open && DueOn < today;
}

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanTier> PlanTiers => Set<PlanTier>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans");
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Tiers).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanTier>(e =>
        {
            e.ToTable("plan_tiers");
            e.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
        });

        modelBuilder.Entity<Contract>(e =>
        {
            e.ToTable("contracts");
            e.Property(x => x.PayerName).HasMaxLength(200);
            e.Property(x => x.PayerDocument).HasMaxLength(14);
            e.Property(x => x.PayerContactName).HasMaxLength(200);
            e.Property(x => x.PayerEmail).HasMaxLength(254);
            e.Property(x => x.PayerPhone).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.ToTable("invoices");
            e.Property(x => x.TierLabel).HasMaxLength(60);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.Property(x => x.InvoiceNumber).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // Uma fatura válida por contrato e mês; cancelada pode ser gerada de novo (ex.: valor corrigido).
            e.HasIndex(x => new { x.ContractId, x.ReferenceMonth }).IsUnique().HasFilter("status <> 'Cancelled'");
            e.HasIndex(x => new { x.Status, x.DueOn });
        });
    }
}

public sealed class BillingDbContextDesignFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSineduPostgres("Host=localhost;Database=design", BillingDbContext.Schema).Options);
}
