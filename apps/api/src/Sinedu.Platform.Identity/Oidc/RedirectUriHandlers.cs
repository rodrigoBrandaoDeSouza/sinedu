using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Sinedu.Platform.Identity.Oidc;

/// <summary>
/// Substitui a validação padrão de redirect_uri. Para o cliente das escolas, aceita
/// {escola}/signin-oidc apenas de escolas ATIVAS no registro; para os demais, a lista cadastrada.
/// </summary>
internal sealed class ValidateRedirectUriHandler(
    IOpenIddictApplicationManager applications, ITenantDirectory tenants, HostClassifier classifier, IOptions<SineduUrls> urls)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseScopedHandler<ValidateRedirectUriHandler>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ClientId == OidcClients.Tenant)
        {
            var slug = OidcClients.TenantSlugFromRedirect(context.RedirectUri, urls.Value, classifier, OidcClients.CallbackPath);
            var tenant = slug is null ? null : await tenants.FindBySlugAsync(slug, context.CancellationToken);
            if (tenant is not { Status: TenantStatus.Active })
            {
                context.Reject(Errors.InvalidRequest, "redirect_uri não corresponde a uma escola ativa.");
            }

            return;
        }

        var application = await applications.FindByClientIdAsync(context.ClientId!, context.CancellationToken);
        if (application is null || string.IsNullOrEmpty(context.RedirectUri)
            || !await applications.ValidateRedirectUriAsync(application, context.RedirectUri, context.CancellationToken))
        {
            context.Reject(Errors.InvalidRequest, "redirect_uri inválido para este cliente.");
        }
    }
}

/// <summary>Mesma regra para o post_logout_redirect_uri.</summary>
internal sealed class ValidatePostLogoutRedirectUriHandler(
    IOpenIddictApplicationManager applications, ITenantDirectory tenants, HostClassifier classifier, IOptions<SineduUrls> urls)
    : IOpenIddictServerHandler<ValidateEndSessionRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateEndSessionRequestContext>()
            .UseScopedHandler<ValidatePostLogoutRedirectUriHandler>()
            .SetOrder(OpenIddictServerHandlers.Session.ValidateClientPostLogoutRedirectUri.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateEndSessionRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var uri = context.PostLogoutRedirectUri;
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        var slug = OidcClients.TenantSlugFromRedirect(uri, urls.Value, classifier, OidcClients.SignedOutPath);
        if (slug is not null && await tenants.FindBySlugAsync(slug, context.CancellationToken) is not null)
        {
            return;
        }

        var panel = await applications.FindByClientIdAsync(OidcClients.Panel, context.CancellationToken);
        if (panel is not null && await applications.ValidatePostLogoutRedirectUriAsync(panel, uri, context.CancellationToken))
        {
            return;
        }

        context.Reject(Errors.InvalidRequest, "post_logout_redirect_uri inválido.");
    }
}
