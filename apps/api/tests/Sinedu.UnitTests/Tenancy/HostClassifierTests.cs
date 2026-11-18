using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.UnitTests.Tenancy;

public class HostClassifierTests
{
    private readonly HostClassifier _classifier = new("sinedu.com.br");

    [Theory]
    [InlineData("escola-x.sinedu.com.br", "escola-x")]
    [InlineData("ESCOLA-X.Sinedu.com.br", "escola-x")]
    [InlineData("colegio123.sinedu.com.br:443", "colegio123")]
    [InlineData("abc.sinedu.com.br", "abc")]
    public void Classifica_subdominio_de_escola(string host, string expectedSlug)
    {
        var result = _classifier.Classify(host);

        Assert.Equal(HostKind.Tenant, result.Kind);
        Assert.Equal(expectedSlug, result.TenantSlug);
    }

    [Theory]
    [InlineData("conta.sinedu.com.br", HostKind.Account)]
    [InlineData("painel.sinedu.com.br", HostKind.Panel)]
    [InlineData("www.sinedu.com.br", HostKind.Reserved)]
    [InlineData("api.sinedu.com.br", HostKind.Reserved)]
    [InlineData("suporte.sinedu.com.br", HostKind.Reserved)]
    public void Classifica_subdominios_da_plataforma_e_reservados(string host, HostKind expected)
    {
        var result = _classifier.Classify(host);

        Assert.Equal(expected, result.Kind);
        Assert.Null(result.TenantSlug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sinedu.com.br")]
    [InlineData("escola.outrodominio.com.br")]
    [InlineData("escola.sinedu.com.br.atacante.com")]
    [InlineData("a.b.sinedu.com.br")]
    [InlineData("ab.sinedu.com.br")]
    [InlineData("-escola.sinedu.com.br")]
    [InlineData("escola-.sinedu.com.br")]
    [InlineData("esc--ola.sinedu.com.br")]
    [InlineData("esc_ola.sinedu.com.br")]
    [InlineData("xn--escla-sva.sinedu.com.br")]
    public void Rejeita_hosts_invalidos(string? host)
    {
        Assert.Equal(HostKind.Unknown, _classifier.Classify(host).Kind);
    }

    [Fact]
    public void Funciona_com_localhost_em_desenvolvimento()
    {
        var dev = new HostClassifier("localhost");

        Assert.Equal("escola-demo", dev.Classify("escola-demo.localhost:5173").TenantSlug);
        Assert.Equal(HostKind.Panel, dev.Classify("painel.localhost:5174").Kind);
        Assert.Equal(HostKind.Unknown, dev.Classify("localhost:5200").Kind);
    }
}
