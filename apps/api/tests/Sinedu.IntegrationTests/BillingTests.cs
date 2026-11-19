using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Cobrança manual (P-06..P-09, D16): plano por faixa de alunos, contrato da escola com a APMF pagante, fatura mensal
/// idempotente com a contagem usada, baixa manual, NF opcional e alertas. Só SUPER_ADMIN altera; SUPORTE só vê alertas.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class BillingTests(SineduFixture fx)
{
    private static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    [Fact]
    public async Task Suporte_so_ve_alertas_e_anonimo_nao_entra()
    {
        var (_, support) = await fx.PanelClientAsync(Roles.Support);
        foreach (var path in new[] { "/api/billing/plans", "/api/billing/contracts", "/api/billing/invoices" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/billing/invoices/generate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri("/api/billing/alerts", UriKind.Relative))).StatusCode);

        using var anonymous = fx.PlatformClient("painel.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/billing/plans", UriKind.Relative))).StatusCode);
        using var school = fx.SchoolClient(SineduFixture.SchoolA, await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]));
        Assert.Equal(HttpStatusCode.NotFound, (await school.GetAsync(new Uri("/api/billing/plans", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Plano_valida_as_faixas()
    {
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var twoOpen = await admin.PostAsJsonAsync("/api/billing/plans", new { name = Unique("Plano"), tiers = new object[] { new { upToStudents = (int?)null, monthlyPrice = 10 }, new { upToStudents = (int?)null, monthlyPrice = 20 } } });
        Assert.Equal(HttpStatusCode.BadRequest, twoOpen.StatusCode);
        var repeated = await admin.PostAsJsonAsync("/api/billing/plans", new { name = Unique("Plano"), tiers = new object[] { new { upToStudents = 100, monthlyPrice = 10 }, new { upToStudents = 100, monthlyPrice = 20 } } });
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        var empty = await admin.PostAsJsonAsync("/api/billing/plans", new { name = Unique("Plano"), tiers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Fatura_mensal_usa_a_faixa_da_contagem_e_e_idempotente()
    {
        await SchoolData.EnsureAsync(fx, SineduFixture.SchoolA);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await CollectMetricsAsync(admin);

        // Até 1 aluno R$ 100; até 1.000 R$ 250; acima disso R$ 400. A escola A tem pelo menos 2 alunos ativos.
        var planId = await CreatePlanAsync(admin, (1, 100m), (1000, 250m), (null, 400m));
        var month = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var contractId = await CreateContractAsync(admin, SineduFixture.SchoolA, planId, month, endsOn: null, dueDay: 28);

        var generated = await (await admin.PostAsJsonAsync("/api/billing/invoices/generate", new { month = Month(month) })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(generated.GetProperty("created").GetInt32() >= 1);
        var again = await (await admin.PostAsJsonAsync("/api/billing/invoices/generate", new { month = Month(month) })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("created").GetInt32());

        var invoice = await InvoiceAsync(admin, contractId, month);
        Assert.Equal(250m, invoice.GetProperty("amount").GetDecimal());
        Assert.Equal("2 a 1000 alunos", invoice.GetProperty("tierLabel").GetString());
        Assert.True(invoice.GetProperty("students").GetInt32() >= 2);
        Assert.Equal(new DateOnly(month.Year, month.Month, 28), DateOnly.Parse(invoice.GetProperty("dueOn").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal("Associação de Pais, Mestres e Funcionários (fictícia)", invoice.GetProperty("payer").GetString());

        // Mês passado com vencimento no dia 28: já venceu → alerta de fatura vencida.
        var id = invoice.GetProperty("id").GetGuid();
        if (Today > new DateOnly(month.Year, month.Month, 28))
        {
            Assert.Equal("Overdue", invoice.GetProperty("status").GetString());
            var alerts = await admin.GetFromJsonAsync<JsonElement>("/api/billing/alerts");
            Assert.Contains(alerts.EnumerateArray(), a => a.GetProperty("kind").GetString() == "fatura_vencida" && a.GetProperty("invoiceId").GetGuid() == id);
        }

        // NF opcional, baixa manual; depois de paga não volta a receber baixa nem é cancelada.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/billing/invoices/{id}/fiscal-note", new { invoiceNumber = "NF-123", issuedOn = Today })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/billing/invoices/{id}/pay", new { paidOn = Today, paidAmount = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/billing/invoices/{id}/pay", new { paidOn = Today, paidAmount = 250 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/billing/invoices/{id}/pay", new { paidOn = Today, paidAmount = 250 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/billing/invoices/{id}/cancel", new { reason = "Teste de cancelamento" })).StatusCode);
        var paid = await InvoiceAsync(admin, contractId, month);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal("NF-123", paid.GetProperty("invoiceNumber").GetString());

        // Cancelada, a fatura do mês pode ser gerada de novo.
        var next = month.AddMonths(1);
        await admin.PostAsJsonAsync("/api/billing/invoices/generate", new { month = Month(next) });
        var open = await InvoiceAsync(admin, contractId, next);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/billing/invoices/{open.GetProperty("id").GetGuid()}/cancel", new { reason = "Valor corrigido" })).StatusCode);
        var regenerated = await (await admin.PostAsJsonAsync("/api/billing/invoices/generate", new { month = Month(next) })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, regenerated.GetProperty("created").GetInt32());

        // Um contrato ativo por escola no mesmo período.
        var duplicate = await admin.PostAsJsonAsync("/api/billing/contracts", ContractBody(SineduFixture.SchoolA, await TenantIdAsync(SineduFixture.SchoolA), planId, month, null, 10));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Alertas_de_limite_contrato_vencendo_e_escola_sem_contrato()
    {
        await SchoolData.EnsureAsync(fx, SineduFixture.SchoolB);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await CollectMetricsAsync(admin);
        var tenantB = await TenantIdAsync(SineduFixture.SchoolB);
        var tenantC = await TenantIdAsync(SineduFixture.SchoolC);

        // Plano só até 1 aluno: a escola B (2+ alunos) passa do limite. Contrato termina em 10 dias.
        var planId = await CreatePlanAsync(admin, (1, 50m));
        await CreateContractAsync(admin, SineduFixture.SchoolB, planId, Today.AddMonths(-2), endsOn: Today.AddDays(10), dueDay: 5);

        var alerts = (await admin.GetFromJsonAsync<JsonElement>("/api/billing/alerts")).EnumerateArray().ToList();
        Assert.Contains(alerts, a => a.GetProperty("kind").GetString() == "limite_excedido" && a.GetProperty("tenantId").GetGuid() == tenantB);
        Assert.Contains(alerts, a => a.GetProperty("kind").GetString() == "contrato_vencendo" && a.GetProperty("tenantId").GetGuid() == tenantB);
        Assert.DoesNotContain(alerts, a => a.GetProperty("kind").GetString() == "sem_contrato" && a.GetProperty("tenantId").GetGuid() == tenantB);
        Assert.Contains(alerts, a => a.GetProperty("kind").GetString() == "sem_contrato" && a.GetProperty("tenantId").GetGuid() == tenantC);

        // Alertas não carregam dado de aluno: só escola, contagem e datas.
        Assert.All(alerts, a => Assert.DoesNotContain("FICTÍCI", a.GetProperty("message").GetString(), StringComparison.Ordinal));
    }

    private static async Task CollectMetricsAsync(HttpClient admin) =>
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(new Uri("/api/metrics/collect", UriKind.Relative), null)).StatusCode);

    private static async Task<Guid> CreatePlanAsync(HttpClient admin, params (int? UpTo, decimal Price)[] tiers)
    {
        var response = await admin.PostAsJsonAsync("/api/billing/plans", new
        {
            name = Unique("Plano de teste"),
            tiers = tiers.Select(t => new { upToStudents = t.UpTo, monthlyPrice = t.Price }),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateContractAsync(HttpClient admin, string slug, Guid planId, DateOnly startsOn, DateOnly? endsOn, int dueDay)
    {
        var response = await admin.PostAsJsonAsync("/api/billing/contracts", ContractBody(slug, await TenantIdAsync(slug), planId, startsOn, endsOn, dueDay));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object ContractBody(string slug, Guid tenantId, Guid planId, DateOnly startsOn, DateOnly? endsOn, int dueDay) => new
    {
        tenantId,
        planId,
        payerName = "Associação de Pais, Mestres e Funcionários (fictícia)",
        payerDocument = "00.000.000/0001-91",
        payerContactName = "Tesouraria fictícia",
        payerEmail = $"apmf@{slug}.local",
        startsOn,
        endsOn,
        dueDay,
    };

    private static async Task<JsonElement> InvoiceAsync(HttpClient admin, Guid contractId, DateOnly month)
    {
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/billing/invoices?month={Month(month)}");
        return list.EnumerateArray().Single(i => i.GetProperty("contractId").GetGuid() == contractId && i.GetProperty("status").GetString() != "Cancelled");
    }

    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        return await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];
}
