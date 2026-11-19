using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Support;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Acesso de suporte (P-11, D17): pedido no painel → só o Diretor aprova na escola → papel temporário somente leitura
/// de dados comuns → tudo auditado → revogação imediata. Nunca vale em outra escola nem alcança dados protegidos.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class SupportAccessTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;
    private const string B = SineduFixture.SchoolB;

    [Fact]
    public async Task Fluxo_completo_pedido_aprovacao_leitura_e_revogacao()
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        var (supportUser, panel) = await fx.PanelClientAsync(Roles.Support);
        var requestId = await RequestAsync(panel, A, "Conferir a importação de turmas que falhou", 4);

        // Os diretores são avisados por e-mail; o pedido aparece na tela da escola.
        var director = await fx.SignInAsync(A, [Roles.Director]);
        using var directorClient = fx.SchoolClient(A, director);
        var pending = await GrantAsync(directorClient, requestId);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Contains(fx.Email.Sent, m => m.Subject.StartsWith("Pedido de acesso de suporte", StringComparison.Ordinal)
            && m.TextBody.Contains("Conferir a importação", StringComparison.Ordinal));

        // Sem aprovação, a conta da plataforma não entra.
        Assert.Null(await ActiveGrantAsync(supportUser, A));

        Assert.Equal(HttpStatusCode.NoContent, (await directorClient.PostAsync(new Uri($"/api/access/support/{requestId}/approve", UriKind.Relative), null)).StatusCode);
        var grant = await ActiveGrantAsync(supportUser, A);
        Assert.NotNull(grant);
        Assert.InRange(grant.ExpiresAt, DateTimeOffset.UtcNow.AddHours(3.9), DateTimeOffset.UtcNow.AddHours(4.1));

        // Somente leitura de dados comuns.
        var session = await fx.SessionForMemberAsync(A, supportUser);
        using var support = fx.SchoolClient(A, session);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri("/api/students", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri($"/api/students/{sample.StudentInX}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri("/api/occurrences", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri($"/api/students/{sample.StudentInX}/health", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri("/api/access/support", UriKind.Relative))).StatusCode);
        var write = await support.PostAsJsonAsync("/api/access/invitations", new { email = "x@escola-a.local", fullName = "Qualquer", role = Roles.Teacher });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains("somente leitura", await write.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Tudo auditado como sessão de suporte, sem conteúdo; o Diretor vê a contagem.
        await using (var audit = await AuditAsync(A))
        {
            var events = await audit.Events.AsNoTracking().Where(e => e.UserId == supportUser).ToListAsync();
            Assert.True(events.Count >= 5);
            Assert.All(events, e => Assert.True(e.IsSupportSession));
            Assert.Contains(events, e => e.StudentId == sample.StudentInX && e.ResourceId == "/api/students/{id:guid}");
            Assert.Contains(events, e => e.Result == AuditResult.Denied && e.ResourceType == "POST");
        }

        var approved = await GrantAsync(directorClient, requestId);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.True(approved.GetProperty("actions").GetInt32() >= 5);

        // Revogação: a sessão cai na hora e o login único deixa de aceitar.
        Assert.Equal(HttpStatusCode.NoContent, (await directorClient.PostAsync(new Uri($"/api/access/support/{requestId}/revoke", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await support.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
        Assert.Null(await ActiveGrantAsync(supportUser, A));
        Assert.Equal("Revoked", (await GrantAsync(directorClient, requestId)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(Roles.TechAdmin)]
    [InlineData(Roles.Pedagogue)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.ReadOnly)]
    [InlineData(Roles.PlatformSupport)]
    public async Task So_o_Diretor_aprova_recusa_ou_revoga(string role)
    {
        var (_, panel) = await fx.PanelClientAsync(Roles.Support);
        var requestId = await RequestAsync(panel, A, "Investigar lentidão na lista de alunos", 2);

        var session = await fx.SignInAsync(A, [role]);
        using var client = fx.SchoolClient(A, session);
        foreach (var action in new[] { "approve", "deny", "revoke" })
        {
            var response = await client.PostAsync(new Uri($"/api/access/support/{requestId}/{action}", UriKind.Relative), null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Recusa_nao_libera_e_liberacao_de_uma_escola_nao_vale_em_outra()
    {
        var (supportUser, panel) = await fx.PanelClientAsync(Roles.Support);
        var denied = await RequestAsync(panel, A, "Ajuda com o cadastro de turmas", 1);
        var approved = await RequestAsync(panel, B, "Ajuda com a importação da escola B", 1);

        using var directorA = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        await GrantAsync(directorA, denied);
        Assert.Equal(HttpStatusCode.NoContent, (await directorA.PostAsync(new Uri($"/api/access/support/{denied}/deny", UriKind.Relative), null)).StatusCode);
        Assert.Null(await ActiveGrantAsync(supportUser, A));

        using var directorB = fx.SchoolClient(B, await fx.SignInAsync(B, [Roles.Director]));
        await GrantAsync(directorB, approved);
        Assert.Equal(HttpStatusCode.NoContent, (await directorB.PostAsync(new Uri($"/api/access/support/{approved}/approve", UriKind.Relative), null)).StatusCode);

        // Pedido da escola B não aparece nem pode ser decidido na escola A.
        var listA = await directorA.GetFromJsonAsync<JsonElement>("/api/access/support");
        Assert.DoesNotContain(listA.GetProperty("grants").EnumerateArray(), g => g.GetProperty("id").GetGuid() == approved);
        Assert.Equal(HttpStatusCode.NotFound, (await directorA.PostAsync(new Uri($"/api/access/support/{approved}/revoke", UriKind.Relative), null)).StatusCode);

        // Liberado em B; em A continua sem acesso (nem login único, nem papel no banco de A).
        Assert.NotNull(await ActiveGrantAsync(supportUser, B));
        Assert.Null(await ActiveGrantAsync(supportUser, A));
        using var inA = fx.SchoolClient(A, await fx.SessionForMemberAsync(A, supportUser));
        Assert.Equal(HttpStatusCode.Unauthorized, (await inA.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Painel_valida_o_pedido_e_cada_suporte_ve_so_os_proprios()
    {
        var (_, panel) = await fx.PanelClientAsync(Roles.Support);
        var (_, other) = await fx.PanelClientAsync(Roles.Support);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var tenantA = await TenantIdAsync(A);

        Assert.Equal(HttpStatusCode.BadRequest, (await panel.PostAsJsonAsync("/api/support/requests", new { tenantId = tenantA, reason = "Motivo suficiente aqui", durationHours = 25 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await panel.PostAsJsonAsync("/api/support/requests", new { tenantId = tenantA, reason = "curto", durationHours = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await panel.PostAsJsonAsync("/api/support/requests", new { tenantId = Guid.NewGuid(), reason = "Motivo suficiente aqui", durationHours = 2 })).StatusCode);

        var id = await RequestAsync(panel, A, "Motivo suficiente aqui", 2);
        Assert.Equal(HttpStatusCode.Conflict, (await panel.PostAsJsonAsync("/api/support/requests", new { tenantId = tenantA, reason = "Outro pedido igual", durationHours = 2 })).StatusCode);

        var mine = await panel.GetFromJsonAsync<JsonElement>("/api/support/requests");
        Assert.Contains(mine.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        var others = await other.GetFromJsonAsync<JsonElement>("/api/support/requests");
        Assert.DoesNotContain(others.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/support/requests");
        Assert.Contains(all.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);

        // Outro suporte não retira o pedido alheio; o dono retira enquanto pendente.
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(new Uri($"/api/support/requests/{id}/cancel", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await panel.PostAsync(new Uri($"/api/support/requests/{id}/cancel", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Endpoints_de_suporte_respeitam_host_e_autenticacao()
    {
        using var anonymous = fx.PlatformClient("painel.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/support/requests", UriKind.Relative))).StatusCode);

        // API interna: só no conta. e só com token de serviço.
        using var account = fx.PlatformClient("conta.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.GetAsync(new Uri($"/api/internal/support-requests?tenantSlug={A}", UriKind.Relative))).StatusCode);
        var (_, panel) = await fx.PanelClientAsync(Roles.SuperAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await panel.GetAsync(new Uri($"/api/internal/support-requests?tenantSlug={A}", UriKind.Relative))).StatusCode);

        // A escola não expõe o painel.
        using var school = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        Assert.Equal(HttpStatusCode.NotFound, (await school.GetAsync(new Uri("/api/support/requests", UriKind.Relative))).StatusCode);
    }

    private async Task<Guid> RequestAsync(HttpClient panel, string slug, string reason, int hours)
    {
        var response = await panel.PostAsJsonAsync("/api/support/requests", new { tenantId = await TenantIdAsync(slug), reason, durationHours = hours });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GrantAsync(HttpClient director, Guid id)
    {
        var list = await director.GetFromJsonAsync<JsonElement>("/api/access/support");
        Assert.True(list.GetProperty("synced").GetBoolean());
        return list.GetProperty("grants").EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id);
    }

    private async Task<SupportGrantInfo?> ActiveGrantAsync(Guid userId, string slug)
    {
        await using var scope = fx.Platform.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISupportAccessDirectory>().ActiveGrantAsync(userId, await TenantIdAsync(slug));
    }

    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        return await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();
    }

    private async Task<AuditDbContext> AuditAsync(string slug) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(slug, owner: true), AuditDbContext.Schema).Options);
}
