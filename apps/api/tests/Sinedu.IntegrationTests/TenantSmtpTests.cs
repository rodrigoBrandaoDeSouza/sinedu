using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>SMTP por escola configurado no painel e área de testes de envio.</summary>
[Collection(SineduTestGroup.Name)]
public class TenantSmtpTests(SineduFixture fx)
{
    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        return await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync();
    }

    private static object Smtp(string host, string? password) =>
        new { host, port = 587, useTls = true, username = "envios@escola.test", password, fromAddress = "nao-responda@escola.test", fromName = "Escola de Teste" };

    [Fact]
    public async Task Super_admin_configura_e_a_senha_nunca_volta_para_a_tela()
    {
        var id = await TenantIdAsync(SineduFixture.SchoolA);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/tenants/{id}/smtp", Smtp("smtp.escola-a.test", "S3nha-Secreta-123"))).StatusCode);

        var response = await admin.GetAsync(new Uri($"/api/tenants/{id}/smtp", UriKind.Relative));
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("S3nha-Secreta-123", text, StringComparison.Ordinal);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.True(body.GetProperty("configured").GetBoolean());
        Assert.True(body.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("smtp.escola-a.test", body.GetProperty("host").GetString());

        // Senha em branco ao editar mantém a atual.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/tenants/{id}/smtp", Smtp("smtp2.escola-a.test", null))).StatusCode);
        var stored = TenantSmtp.FromSecret(await fx.Vault.ReadAsync(TenantSmtp.SecretPath(id)));
        Assert.Equal("smtp2.escola-a.test", stored!.Host);
        Assert.Equal("S3nha-Secreta-123", stored.Password);
    }

    [Fact]
    public async Task Suporte_nao_ve_nem_altera_smtp()
    {
        var id = await TenantIdAsync(SineduFixture.SchoolA);
        var (_, support) = await fx.PanelClientAsync(Roles.Support);

        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri($"/api/tenants/{id}/smtp", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PutAsJsonAsync($"/api/tenants/{id}/smtp", Smtp("x.test", "y"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/tenants/email-test", new { to = "a@b.test" })).StatusCode);
    }

    [Fact]
    public async Task Emails_da_escola_saem_pelo_smtp_dela_e_as_outras_usam_o_padrao()
    {
        var idC = await TenantIdAsync(SineduFixture.SchoolC);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await admin.PutAsJsonAsync($"/api/tenants/{idC}/smtp", Smtp("smtp.escola-c.test", "senha-c"));

        async Task<(SmtpOptions Options, string Source)> ResolveInSchoolAsync(string slug)
        {
            await using var scope = fx.School.Services.CreateAsyncScope();
            var resolution = await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
            scope.ServiceProvider.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
            return await scope.ServiceProvider.GetRequiredService<SmtpSettingsResolver>().ResolveAsync(new EmailMessage("familia@exemplo.test", "x", "y"));
        }

        var c = await ResolveInSchoolAsync(SineduFixture.SchoolC);
        Assert.Equal(("smtp.escola-c.test", "escola"), (c.Options.Host, c.Source));

        var b = await ResolveInSchoolAsync(SineduFixture.SchoolB);
        Assert.Equal("padrão", b.Source);
    }

    [Fact]
    public async Task Area_de_testes_envia_pelo_smtp_escolhido_e_mostra_o_resultado()
    {
        var id = await TenantIdAsync(SineduFixture.SchoolA);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await admin.PutAsJsonAsync($"/api/tenants/{id}/smtp", Smtp("smtp.salvo.test", "senha-salva"));
        var to = $"teste-{Guid.NewGuid():N}@exemplo.test";

        var saved = await (await admin.PostAsJsonAsync("/api/tenants/email-test", new { tenantId = id, to })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(saved.GetProperty("ok").GetBoolean());
        Assert.Equal("SMTP da escola", saved.GetProperty("source").GetString());
        var sent = Assert.Single(fx.Email.Sent, m => m.To == to);
        Assert.Equal("smtp.salvo.test", sent.Smtp!.Host);

        // Testar antes de salvar: usa o formulário, mantendo a senha salva se o campo vier vazio.
        var to2 = $"teste-{Guid.NewGuid():N}@exemplo.test";
        var draft = await (await admin.PostAsJsonAsync("/api/tenants/email-test", new { tenantId = id, to = to2, smtp = Smtp("smtp.rascunho.test", null) })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("configuração do formulário", draft.GetProperty("source").GetString(), StringComparison.Ordinal);
        var draftMessage = Assert.Single(fx.Email.Sent, m => m.To == to2);
        Assert.Equal(("smtp.rascunho.test", "senha-salva"), (draftMessage.Smtp!.Host, draftMessage.Smtp.Password));

        // Falha: mostra o motivo sem a senha.
        var to3 = $"teste-{Guid.NewGuid():N}@exemplo.test";
        fx.Email.FailuresFor[to3] = 1;
        var failed = await (await admin.PostAsJsonAsync("/api/tenants/email-test", new { tenantId = id, to = to3 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("senha-salva", failed.GetProperty("error").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/tenants/email-test", new { tenantId = id, to = "invalido" })).StatusCode);
    }

    [Fact]
    public async Task Remover_volta_ao_padrao_da_plataforma()
    {
        var id = await TenantIdAsync(SineduFixture.SchoolB);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await admin.PutAsJsonAsync($"/api/tenants/{id}/smtp", Smtp("smtp.b.test", "b"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(new Uri($"/api/tenants/{id}/smtp", UriKind.Relative))).StatusCode);
        var body = await admin.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/smtp");
        Assert.False(body.GetProperty("configured").GetBoolean());
    }
}
