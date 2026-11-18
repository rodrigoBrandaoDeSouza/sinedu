using System.Net;
using System.Net.Http.Json;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.IntegrationTests;

[Collection(SineduTestGroup.Name)]
public class HostRoutingTests(SineduFixture fx)
{
    [Fact]
    public async Task Escola_e_resolvida_pelo_subdominio_no_registro()
    {
        using var client = fx.SchoolClient(SineduFixture.SchoolA);

        var health = await client.GetFromJsonAsync<Health>("/api/health");

        Assert.Equal(SineduFixture.SchoolA, health!.Tenant);
    }

    [Theory]
    [InlineData("nao-existe.localhost")]
    [InlineData("painel.localhost")]
    [InlineData("conta.localhost")]
    [InlineData("www.localhost")]
    [InlineData("localhost")]
    [InlineData("escola-a.outro-dominio.com")]
    public async Task Host_da_escola_responde_404_para_o_que_nao_e_escola_registrada(string host)
    {
        using var client = fx.SchoolClient(SineduFixture.SchoolA);
        client.DefaultRequestHeaders.Host = host;

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Escola_suspensa_fica_indisponivel_sem_login()
    {
        const string slug = SineduFixture.SchoolB;
        await fx.SetTenantStatusAsync(slug, TenantStatus.Suspended);
        try
        {
            using var client = fx.SchoolClient(slug);
            var response = await client.GetAsync(new Uri("/auth/login", UriKind.Relative));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("tenant_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await fx.SetTenantStatusAsync(slug, TenantStatus.Active);
        }
    }

    [Theory]
    [InlineData("painel.localhost", "painel")]
    [InlineData("conta.localhost", "conta")]
    public async Task Host_da_plataforma_atende_conta_e_painel(string host, string surface)
    {
        using var client = fx.PlatformClient(host);

        var health = await client.GetFromJsonAsync<Health>("/api/health");

        Assert.Equal(surface, health!.Surface);
    }

    [Theory]
    [InlineData("escola-a.localhost", "/api/health")]
    [InlineData("painel.localhost", "/.well-known/openid-configuration")]
    [InlineData("painel.localhost", "/connect/authorize")]
    [InlineData("conta.localhost", "/api/tenants")]
    public async Task Host_da_plataforma_so_expoe_cada_superficie_no_host_certo(string host, string path)
    {
        using var client = fx.PlatformClient(host);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Respostas_tem_cabecalhos_de_seguranca()
    {
        using var client = fx.SchoolClient(SineduFixture.SchoolA);

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.False(response.Headers.Contains("Server"));
    }

    private sealed record Health(string Status, string? Tenant, string? Surface);
}
