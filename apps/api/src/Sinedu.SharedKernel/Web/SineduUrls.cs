using Microsoft.Extensions.Options;

namespace Sinedu.SharedKernel.Web;

/// <summary>Endereços públicos das superfícies. Em produção vêm do domínio real; em dev, de *.localhost.</summary>
public sealed class SineduUrls
{
    public const string Section = "Sinedu";

    public string BaseDomain { get; set; } = "";

    /// <summary>Ex.: https://conta.sinedu.com.br (também é o issuer do OIDC).</summary>
    public Uri AccountUrl { get; set; } = new("http://conta.localhost:5175");

    /// <summary>Ex.: https://painel.sinedu.com.br</summary>
    public Uri PanelUrl { get; set; } = new("http://painel.localhost:5174");

    /// <summary>Ex.: https://{slug}.sinedu.com.br</summary>
    public string TenantUrlTemplate { get; set; } = "http://{slug}.localhost:5173";

    /// <summary>
    /// Endereço interno para chamadas servidor→conta (descoberta OIDC, troca de código). Opcional;
    /// em dev aponta para o Kestrel do Platform.Host mantendo o Host público.
    /// </summary>
    public Uri? AccountBackchannelUrl { get; set; }

    /// <summary>Cookies com Secure e prefixo __Host-. Desligado apenas em desenvolvimento (HTTP).</summary>
    public bool SecureCookies { get; set; } = true;

    public Uri TenantUrl(string slug) => new(TenantUrlTemplate.Replace("{slug}", slug, StringComparison.Ordinal));

    public string CookieName(string name) => SecureCookies ? "__Host-" + name : name;
}

/// <summary>
/// Redireciona chamadas servidor→conta para o endereço interno, preservando o Host público
/// (o Platform.Host identifica a superfície pelo Host).
/// </summary>
public sealed class AccountBackchannelHandler(IOptions<SineduUrls> urls) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = urls.Value;
        if (options.AccountBackchannelUrl is { } internalUrl
            && request.RequestUri is { } uri
            && string.Equals(uri.Authority, options.AccountUrl.Authority, StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Host = uri.Authority;
            request.RequestUri = new UriBuilder(uri) { Scheme = internalUrl.Scheme, Host = internalUrl.Host, Port = internalUrl.Port }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
