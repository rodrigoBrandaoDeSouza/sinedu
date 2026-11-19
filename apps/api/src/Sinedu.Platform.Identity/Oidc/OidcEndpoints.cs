using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Sinedu.Platform.Identity.Oidc;

/// <summary>Endpoints em modo passthrough do OpenIddict: aqui decidimos quem pode entrar em qual cliente.</summary>
internal static class OidcEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync);
        endpoints.MapPost("/connect/token", TokenAsync);
        endpoints.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], (Delegate)EndSessionAsync);
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext ctx,
        IdentityDbContext db,
        ITenantDirectory tenants,
        HostClassifier classifier,
        IOptions<SineduUrls> urls,
        IAuditLog audit,
        ISupportAccessDirectory support,
        TimeProvider clock)
    {
        var request = ctx.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Requisição OIDC ausente.");
        var session = await ctx.AuthenticateAsync(AccountSchemes.Session);
        var now = clock.GetUtcNow();

        var authTime = session.Succeeded ? AccountClaims.GetAuthTime(session.Principal) : null;
        var expired = authTime is null
            || (request.MaxAge is { } maxAge && now - authTime.Value > TimeSpan.FromSeconds(Math.Max(maxAge, 0)));

        if (expired)
        {
            return RedirectToLogin(ctx, reauth: session.Succeeded);
        }

        var userId = AccountClaims.GetUserId(session.Principal!);
        var user = await db.Users.AsNoTracking().Include(u => u.Memberships).Include(u => u.PlatformRoles)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.Status != AccountStatus.Active || user.SecurityStamp.ToString() != session.Principal!.FindFirstValue(AccountClaims.Stamp))
        {
            await ctx.SignOutAsync(AccountSchemes.Session);
            return RedirectToLogin(ctx, reauth: false);
        }

        var hasMfa = session.Principal!.FindAll(AccountClaims.Amr).Any(c => c.Value == AccountClaims.AmrMfa);
        string? tenantSlug = null;

        if (request.ClientId == OidcClients.Tenant)
        {
            tenantSlug = OidcClients.TenantSlugFromRedirect(request.RedirectUri, urls.Value, classifier, OidcClients.CallbackPath)!;
            var tenant = await tenants.FindBySlugAsync(tenantSlug);
            var membership = tenant is null ? null : user.Memberships.FirstOrDefault(m => m.TenantId == tenant.Id && m.Active);

            // D17/D19: conta da plataforma nunca tem vínculo de escola; só entra com liberação do Diretor vigente.
            var supportGrant = tenant is not null && membership is null && user.Kind == AccountKind.Platform
                ? await support.ActiveGrantAsync(user.Id, tenant.Id)
                : null;

            if (membership is null && supportGrant is null)
            {
                await audit.WriteAsync(new AuditEntry("sso_denied", AuditResult.Denied, "tenant", tenant?.Id.ToString(), UserId: user.Id));
                return Results.Redirect($"/sem-acesso?escola={Uri.EscapeDataString(tenantSlug)}");
            }

            // Papel privilegiado concedido depois do login: exige MFA antes de seguir (suporte sempre é privilegiado).
            if ((supportGrant is not null || membership!.Privileged) && !hasMfa)
            {
                await ctx.SignOutAsync(AccountSchemes.Session);
                return RedirectToLogin(ctx, reauth: true);
            }
        }
        else if (request.ClientId == OidcClients.Panel)
        {
            if (user.Kind != AccountKind.Platform || !user.PlatformRoles.Any(r => r.RevokedAt == null))
            {
                return Results.Redirect("/sem-acesso?painel=1");
            }

            if (!hasMfa)
            {
                await ctx.SignOutAsync(AccountSchemes.Session);
                return RedirectToLogin(ctx, reauth: true);
            }
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.FullName)
            .SetClaims(Claims.AuthenticationMethodReference, [.. session.Principal.FindAll(AccountClaims.Amr).Select(c => c.Value)]);
        identity.AddClaim(new Claim(Claims.AuthenticationTime, authTime!.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        identity.SetScopes(request.GetScopes());
        identity.SetDestinations(_ => [Destinations.IdentityToken, Destinations.AccessToken]);

        await audit.WriteAsync(new AuditEntry("sso_issued", ResourceType: request.ClientId, ResourceId: tenantSlug, UserId: user.Id));
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext ctx, IdentityDbContext db)
    {
        var request = ctx.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Requisição OIDC ausente.");

        if (request.IsAuthorizationCodeGrantType())
        {
            var result = await ctx.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var userId = Guid.TryParse(result.Principal?.GetClaim(Claims.Subject), out var id) ? id : Guid.Empty;
            var active = await db.Users.AnyAsync(u => u.Id == userId && u.Status == AccountStatus.Active);
            if (!active)
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    }));
            }

            return Results.SignIn(result.Principal!, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // Serviço → serviço (escolas chamando a API interna de vínculos).
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId!);
            identity.SetScopes(request.GetScopes().Intersect([OidcClients.MembershipsScope]).ToImmutableArray());
            identity.SetDestinations(_ => [Destinations.AccessToken]);
            return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.BadRequest();
    }

    private static async Task<IResult> EndSessionAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(AccountSchemes.Session);
        await ctx.SignOutAsync(AccountSchemes.Pending);
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>Volta para a mesma requisição de autorização depois do login (inclusive POST, convertido em GET).</summary>
    private static IResult RedirectToLogin(HttpContext ctx, bool reauth)
    {
        var parameters = ctx.Request.HasFormContentType
            ? ctx.Request.Form.Where(p => p.Key != Parameters.MaxAge).Select(p => KeyValuePair.Create(p.Key, p.Value))
            : ctx.Request.Query.Where(p => p.Key != Parameters.MaxAge).Select(p => KeyValuePair.Create(p.Key, p.Value));
        var returnUrl = ctx.Request.PathBase + ctx.Request.Path + QueryString.Create(parameters);
        var target = $"/entrar?returnUrl={Uri.EscapeDataString(returnUrl)}{(reauth ? "&reauth=1" : "")}";
        return Results.Redirect(target);
    }
}
