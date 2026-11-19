using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Redes no painel (P-04, D5): só agrupam escolas. SUPER_ADMIN gerencia; SUPORTE só vê.</summary>
[Collection(SineduTestGroup.Name)]
public class NetworkTests(SineduFixture fx)
{
    [Fact]
    public async Task Suporte_ve_mas_nao_altera_e_anonimo_nao_entra()
    {
        var (_, support) = await fx.PanelClientAsync(Roles.Support);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri("/api/networks", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/networks", new { name = "Rede do suporte", kind = "Estadual" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PutAsJsonAsync($"/api/tenants/{Guid.NewGuid()}/network", new { networkId = (Guid?)null })).StatusCode);

        using var anonymous = fx.PlatformClient("painel.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/networks", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Cria_valida_vincula_e_so_exclui_rede_vazia()
    {
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var name = $"Rede Estadual de Teste {Guid.NewGuid():N}"[..30];

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/networks", new { name = "AB", kind = "Estadual" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/networks", new { name, kind = "Estadual", uf = "Paraná" })).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/networks", new { name, kind = "Estadual", uf = "pr", municipality = "Curitiba" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var networkId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/networks", new { name, kind = "Municipal" })).StatusCode);

        var tenantC = await TenantIdAsync(SineduFixture.SchoolC);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/tenants/{tenantC}/network", new { networkId })).StatusCode);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/networks");
        var row = list.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == networkId);
        Assert.Equal(1, row.GetProperty("schools").GetInt32());
        Assert.Equal("PR", row.GetProperty("uf").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(new Uri($"/api/networks/{networkId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/tenants/{tenantC}/network", new { networkId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(new Uri($"/api/networks/{networkId}", UriKind.Relative))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/tenants/{tenantC}/network", new { networkId = Guid.NewGuid() })).StatusCode);
    }

    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        return await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();
    }
}
