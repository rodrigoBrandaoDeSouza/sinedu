using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Sinedu.Platform.Identity.Oidc;

public static class OidcClients
{
    /// <summary>Cliente único usado por todas as escolas; o redirect_uri identifica a escola.</summary>
    public const string Tenant = OidcContract.TenantClientId;

    public const string Panel = OidcContract.PanelClientId;

    /// <summary>Escopo da API interna usada pelas escolas para sincronizar vínculos e convites.</summary>
    public const string MembershipsScope = OidcContract.MembershipsScope;

    public const string CallbackPath = OidcContract.CallbackPath;

    public const string SignedOutPath = OidcContract.SignedOutPath;

    /// <summary>Extrai a escola de um redirect_uri do cliente das escolas, se ele seguir o modelo configurado.</summary>
    public static string? TenantSlugFromRedirect(string? redirectUri, SineduUrls urls, HostClassifier classifier, string path)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var classification = classifier.Classify(uri.Authority);
        if (classification is not { Kind: HostKind.Tenant, TenantSlug: { } slug })
        {
            return null;
        }

        var expected = new Uri(urls.TenantUrl(slug), path);
        return Uri.Compare(uri, expected, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
            ? slug
            : null;
    }
}

/// <summary>Registra/atualiza os clientes OIDC na inicialização. Segredos vêm da configuração (Vault/variáveis).</summary>
internal sealed class OidcClientRegistrar(IServiceScopeFactory scopes, IConfiguration configuration, IOptions<SineduUrls> urls) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await UpsertAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId = OidcClients.Tenant,
            ClientSecret = Secret("Oidc:TenantClientSecret"),
            ClientType = ClientTypes.Confidential,
            DisplayName = "Sinedu — escolas",
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.ClientCredentials, Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email, Permissions.Scopes.Profile, Permissions.Prefixes.Scope + OidcClients.MembershipsScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        }, cancellationToken);

        var panel = urls.Value.PanelUrl;
        var panelDescriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = OidcClients.Panel,
            ClientSecret = Secret("Oidc:PanelClientSecret"),
            ClientType = ClientTypes.Confidential,
            DisplayName = "Sinedu — painel",
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email, Permissions.Scopes.Profile,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        panelDescriptor.RedirectUris.Add(new Uri(panel, OidcClients.CallbackPath));
        panelDescriptor.PostLogoutRedirectUris.Add(new Uri(panel, OidcClients.SignedOutPath));
        await UpsertAsync(manager, panelDescriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string Secret(string key) =>
        configuration[key] is { Length: >= 32 } value
            ? value
            : throw new InvalidOperationException($"Configuração '{key}' ausente ou curta (mínimo 32 caracteres).");

    private static async Task UpsertAsync(IOpenIddictApplicationManager manager, OpenIddictApplicationDescriptor descriptor, CancellationToken ct)
    {
        var existing = await manager.FindByClientIdAsync(descriptor.ClientId!, ct);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor, ct);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor, ct);
        }
    }
}
