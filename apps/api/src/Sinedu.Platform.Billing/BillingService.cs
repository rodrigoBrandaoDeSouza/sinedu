using Microsoft.EntityFrameworkCore;
using Sinedu.Platform.Metrics;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Billing;

public sealed record TierInput(int? UpToStudents, decimal MonthlyPrice);

public sealed record PlanInput(string Name, IReadOnlyList<TierInput> Tiers, bool IsActive = true);

public sealed record ContractInput(
    Guid TenantId, Guid PlanId, string PayerName, string? PayerDocument, string? PayerContactName, string? PayerEmail, string? PayerPhone,
    DateOnly StartsOn, DateOnly? EndsOn, int DueDay, string? Notes, ContractStatus Status = ContractStatus.Active);

public sealed record PaymentInput(DateOnly PaidOn, decimal PaidAmount, string? InvoiceNumber, DateOnly? IssuedOn, string? Notes);

public sealed record GenerationResult(DateOnly Month, int Created, int AlreadyExisted, IReadOnlyList<string> SkippedWithoutCount);

/// <summary>Alerta de cobrança (P-09). Calculado na hora; a suspensão continua sendo decisão manual (D16).</summary>
public sealed record BillingAlert(string Kind, Guid TenantId, string School, string Message, Guid? ContractId = null, Guid? InvoiceId = null);

/// <summary>Planos, contratos e faturas (P-06..P-09). Controle manual, sem gateway (D16). Só números, nenhum dado de aluno.</summary>
public sealed class BillingService(BillingDbContext db, TenantsDbContext tenants, StudentCounts counts, IAuditLog audit, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-3)).DateTime);

    // ---------- Planos ----------

    public static Dictionary<string, string[]> Validate(PlanInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length is < 2 or > 120) errors["name"] = ["Use de 2 a 120 caracteres."];
        var tiers = input.Tiers ?? [];
        if (tiers.Count == 0) errors["tiers"] = ["Informe pelo menos uma faixa."];
        else if (tiers.Any(t => t.MonthlyPrice < 0 || t.MonthlyPrice > 10_000_000)) errors["tiers"] = ["Valores mensais precisam ser positivos."];
        else if (tiers.Count(t => t.UpToStudents is null) > 1) errors["tiers"] = ["Só a última faixa pode ser \"acima de\"."];
        else
        {
            var limited = tiers.Where(t => t.UpToStudents is not null).Select(t => t.UpToStudents!.Value).ToList();
            if (limited.Any(v => v < 1) || limited.Distinct().Count() != limited.Count) errors["tiers"] = ["Os limites das faixas precisam ser diferentes e maiores que zero."];
        }

        return errors;
    }

    public async Task<Plan> SavePlanAsync(Guid? id, PlanInput input, CancellationToken ct)
    {
        var plan = id is null ? null : await db.Plans.Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null)
        {
            plan = new Plan { Id = Guid.CreateVersion7(), Name = input.Name.Trim(), CreatedAt = clock.GetUtcNow() };
            db.Plans.Add(plan);
        }
        else
        {
            db.PlanTiers.RemoveRange(plan.Tiers);
            plan.Tiers.Clear();
        }

        plan.Name = input.Name.Trim();
        plan.IsActive = input.IsActive;
        foreach (var t in input.Tiers.OrderBy(t => t.UpToStudents ?? int.MaxValue))
        {
            var tier = new PlanTier { Id = Guid.CreateVersion7(), PlanId = plan.Id, UpToStudents = t.UpToStudents, MonthlyPrice = decimal.Round(t.MonthlyPrice, 2) };
            plan.Tiers.Add(tier);
            db.PlanTiers.Add(tier);
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry(id is null ? "plan_created" : "plan_updated", ResourceType: "plan", ResourceId: plan.Id.ToString()), ct);
        return plan;
    }

    // ---------- Contratos ----------

    public async Task<Dictionary<string, string[]>> ValidateAsync(ContractInput input, Guid? id, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!await tenants.Tenants.AnyAsync(t => t.Id == input.TenantId, ct)) errors["tenantId"] = ["Escolha a escola."];
        if (!await db.Plans.AnyAsync(p => p.Id == input.PlanId && (p.IsActive || db.Contracts.Any(c => c.Id == id && c.PlanId == p.Id)), ct)) errors["planId"] = ["Escolha um plano ativo."];
        if (string.IsNullOrWhiteSpace(input.PayerName) || input.PayerName.Trim().Length is < 3 or > 200) errors["payerName"] = ["Informe o nome da APMF."];
        var doc = Digits(input.PayerDocument);
        if (doc is not null && doc.Length != 14) errors["payerDocument"] = ["O CNPJ tem 14 dígitos."];
        if (!string.IsNullOrWhiteSpace(input.PayerEmail) && !System.Net.Mail.MailAddress.TryCreate(input.PayerEmail.Trim(), out _)) errors["payerEmail"] = ["Informe um e-mail válido."];
        if (input.DueDay is < 1 or > 28) errors["dueDay"] = ["Use um dia entre 1 e 28."];
        if (input.EndsOn is { } end && end < input.StartsOn) errors["endsOn"] = ["O fim precisa ser depois do início."];
        if (input.Status == ContractStatus.Active && errors.Count == 0
            && await db.Contracts.AnyAsync(c => c.Id != id && c.TenantId == input.TenantId && c.Status == ContractStatus.Active
                && c.StartsOn <= (input.EndsOn ?? DateOnly.MaxValue) && (c.EndsOn == null || c.EndsOn >= input.StartsOn), ct))
        {
            errors["tenantId"] = ["Esta escola já tem um contrato ativo nesse período."];
        }

        return errors;
    }

    public async Task<Contract?> SaveContractAsync(Guid? id, ContractInput input, CancellationToken ct)
    {
        var contract = id is null ? null : await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (id is not null && contract is null) return null;
        if (contract is null)
        {
            contract = new Contract { Id = Guid.CreateVersion7(), PayerName = "", CreatedAt = clock.GetUtcNow() };
            db.Contracts.Add(contract);
        }

        contract.TenantId = input.TenantId;
        contract.PlanId = input.PlanId;
        contract.PayerName = input.PayerName.Trim();
        contract.PayerDocument = Digits(input.PayerDocument);
        contract.PayerContactName = Trim(input.PayerContactName);
        contract.PayerEmail = Trim(input.PayerEmail)?.ToLowerInvariant();
        contract.PayerPhone = Trim(input.PayerPhone);
        contract.StartsOn = input.StartsOn;
        contract.EndsOn = input.EndsOn;
        contract.DueDay = input.DueDay;
        contract.Status = input.Status;
        contract.Notes = Trim(input.Notes);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry(id is null ? "contract_created" : "contract_updated", ResourceType: "contract", ResourceId: contract.Id.ToString()), ct);
        return contract;
    }

    // ---------- Faturas ----------

    /// <summary>
    /// Gera a fatura do mês para cada contrato vigente (idempotente). Valor = faixa do plano para a última contagem de
    /// alunos ativos da escola. Escola sem contagem ainda fica de fora e aparece no resultado.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(DateOnly? month, CancellationToken ct)
    {
        var reference = FirstDay(month ?? Today);
        var contracts = (await db.Contracts.Include(c => c.Plan!).ThenInclude(p => p.Tiers).Where(c => c.Status == ContractStatus.Active).ToListAsync(ct))
            .Where(c => c.Covers(reference)).ToList();
        var existing = (await db.Invoices.Where(i => i.ReferenceMonth == reference && i.Status != InvoiceStatus.Cancelled).Select(i => i.ContractId).ToListAsync(ct)).ToHashSet();
        var latest = await counts.LatestAsync(ct);
        var names = await tenants.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);

        var created = 0;
        var skipped = new List<string>();
        foreach (var contract in contracts.Where(c => !existing.Contains(c.Id)))
        {
            if (!latest.TryGetValue(contract.TenantId, out var count))
            {
                skipped.Add(names.GetValueOrDefault(contract.TenantId, "—"));
                continue;
            }

            var (tier, label) = Price(contract.Plan!, count.ActiveStudents);
            db.Invoices.Add(new Invoice
            {
                Id = Guid.CreateVersion7(),
                ContractId = contract.Id,
                TenantId = contract.TenantId,
                ReferenceMonth = reference,
                Students = count.ActiveStudents,
                TierLabel = label,
                Amount = tier.MonthlyPrice,
                DueOn = new DateOnly(reference.Year, reference.Month, contract.DueDay),
                Status = InvoiceStatus.Open,
                CreatedAt = clock.GetUtcNow(),
            });
            created++;
        }

        await db.SaveChangesAsync(ct);
        if (created > 0) await audit.WriteAsync(new AuditEntry("invoices_generated", ResourceType: "invoices", ResourceId: reference.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)), ct);
        return new GenerationResult(reference, created, existing.Count, skipped);
    }

    public async Task<Invoice?> PayAsync(Guid id, PaymentInput input, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is not { Status: InvoiceStatus.Open }) return invoice is null ? null : throw new InvalidOperationException("Só faturas em aberto recebem baixa.");
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidOn = input.PaidOn;
        invoice.PaidAmount = decimal.Round(input.PaidAmount, 2);
        invoice.InvoiceNumber = Trim(input.InvoiceNumber) ?? invoice.InvoiceNumber;
        invoice.IssuedOn = input.IssuedOn ?? invoice.IssuedOn;
        invoice.Notes = Trim(input.Notes) ?? invoice.Notes;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("invoice_paid", ResourceType: "invoice", ResourceId: id.ToString()), ct);
        return invoice;
    }

    /// <summary>Registro opcional da nota fiscal (número e data). Vale para fatura aberta ou paga.</summary>
    public async Task<Invoice?> SetFiscalNoteAsync(Guid id, string? number, DateOnly? issuedOn, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return null;
        if (invoice.Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("Fatura cancelada não recebe nota fiscal.");
        invoice.InvoiceNumber = Trim(number);
        invoice.IssuedOn = invoice.InvoiceNumber is null ? null : issuedOn;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("invoice_fiscal_note", ResourceType: "invoice", ResourceId: id.ToString()), ct);
        return invoice;
    }

    public async Task<Invoice?> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is not { Status: InvoiceStatus.Open }) return invoice is null ? null : throw new InvalidOperationException("Só faturas em aberto podem ser canceladas.");
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.Notes = reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("invoice_cancelled", ResourceType: "invoice", ResourceId: id.ToString()), ct);
        return invoice;
    }

    // ---------- Alertas ----------

    public async Task<List<BillingAlert>> AlertsAsync(CancellationToken ct)
    {
        var today = Today;
        var schools = await tenants.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.Plan!).ThenInclude(p => p.Tiers).Where(c => c.Status == ContractStatus.Active).ToListAsync(ct);
        var latest = await counts.LatestAsync(ct);
        var alerts = new List<BillingAlert>();
        string School(Guid id) => schools.GetValueOrDefault(id) ?? "Escola não ativa";

        var overdue = await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Open && i.DueOn < today).OrderBy(i => i.DueOn).ToListAsync(ct);
        alerts.AddRange(overdue.Select(i => new BillingAlert("fatura_vencida", i.TenantId, School(i.TenantId),
            $"Fatura de {i.ReferenceMonth:MM/yyyy} vencida em {i.DueOn:dd/MM/yyyy} ({(today.DayNumber - i.DueOn.DayNumber)} dia(s)).", i.ContractId, i.Id)));

        alerts.AddRange(contracts.Where(c => c.EndsOn is { } end && end >= today && end <= today.AddDays(30))
            .Select(c => new BillingAlert("contrato_vencendo", c.TenantId, School(c.TenantId), $"Contrato termina em {c.EndsOn:dd/MM/yyyy}.", c.Id)));
        alerts.AddRange(contracts.Where(c => c.EndsOn is { } end && end < today)
            .Select(c => new BillingAlert("contrato_vencido", c.TenantId, School(c.TenantId), $"Contrato terminou em {c.EndsOn:dd/MM/yyyy} e continua ativo.", c.Id)));

        var covered = contracts.Where(c => c.Covers(FirstDay(today))).Select(c => c.TenantId).ToHashSet();
        alerts.AddRange(schools.Where(s => !covered.Contains(s.Key))
            .Select(s => new BillingAlert("sem_contrato", s.Key, s.Value, "Escola ativa sem contrato vigente.")));

        foreach (var c in contracts.Where(c => c.Covers(FirstDay(today))))
        {
            if (latest.TryGetValue(c.TenantId, out var count) && c.Plan!.TierFor(count.ActiveStudents) is null)
            {
                var max = c.Plan.Tiers.Max(t => t.UpToStudents);
                alerts.Add(new BillingAlert("limite_excedido", c.TenantId, School(c.TenantId),
                    $"{count.ActiveStudents} alunos ativos, acima da última faixa do plano {c.Plan.Name} (até {max}).", c.Id));
            }
        }

        return alerts;
    }

    // ---------- Apoio ----------

    /// <summary>Faixa e rótulo para a contagem. Acima da última faixa limitada, cobra a maior e o alerta avisa.</summary>
    public static (PlanTier Tier, string Label) Price(Plan plan, int students)
    {
        var ordered = plan.Tiers.OrderBy(t => t.UpToStudents ?? int.MaxValue).ToList();
        var tier = plan.TierFor(students) ?? ordered[^1];
        var index = ordered.IndexOf(tier);
        var previous = index > 0 ? ordered[index - 1].UpToStudents : null;
        return (tier, tier.Label(previous));
    }

    public static DateOnly FirstDay(DateOnly date) => new(date.Year, date.Month, 1);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Digits(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
