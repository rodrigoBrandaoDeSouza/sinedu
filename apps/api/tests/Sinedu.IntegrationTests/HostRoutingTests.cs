using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sinedu.Platform.Host;
using Sinedu.Tenant.Host;

namespace Sinedu.IntegrationTests;

public class HostRoutingTests(
    WebApplicationFactory<TenantHostEntryPoint> tenantFactory,
    WebApplicationFactory<PlatformHostEntryPoint> platformFactory)
    : IClassFixture<WebApplicationFactory<TenantHostEntryPoint>>, IClassFixture<WebApplicationFactory<PlatformHostEntryPoint>>
{
    [Fact]
    public async Task Host_da_escola_resolve_o_tenant_pelo_subdominio()
    {
        using var client = CreateClient(tenantFactory, "escola-demo.localhost");

        var response = await client.GetFromJsonAsync<HealthResponse>("/api/health");

        Assert.Equal("escola-demo", response!.Tenant);
    }

    [Theory]
    [InlineData("painel.localhost")]
    [InlineData("conta.localhost")]
    [InlineData("www.localhost")]
    [InlineData("localhost")]
    [InlineData("escola.outro-dominio.com")]
    public async Task Host_da_escola_recusa_hosts_que_nao_sao_de_escola(string host)
    {
        using var client = CreateClient(tenantFactory, host);

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("painel.localhost", "painel")]
    [InlineData("conta.localhost", "conta")]
    public async Task Host_da_plataforma_atende_conta_e_painel(string host, string surface)
    {
        using var client = CreateClient(platformFactory, host);

        var response = await client.GetFromJsonAsync<HealthResponse>("/api/health");

        Assert.Equal(surface, response!.Surface);
    }

    [Fact]
    public async Task Host_da_plataforma_recusa_subdominio_de_escola()
    {
        using var client = CreateClient(platformFactory, "escola-demo.localhost");

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Respostas_tem_cabecalhos_de_seguranca()
    {
        using var client = CreateClient(tenantFactory, "escola-demo.localhost");

        var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.False(response.Headers.Contains("Server"));
    }

    private static HttpClient CreateClient<T>(WebApplicationFactory<T> factory, string host)
        where T : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    private sealed record HealthResponse(string Status, string? Tenant, string? Surface);
}
