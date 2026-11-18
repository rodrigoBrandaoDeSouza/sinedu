using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using Sinedu.Platform.Identity.Oidc;
using Sinedu.Platform.Identity.Security;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Identity;

public sealed class IdentityModule : IModule
{
    public const string SensitiveRateLimit = "sensitive";

    public string Name => "Identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");
        var urls = configuration.GetSection(SineduUrls.Section).Get<SineduUrls>() ?? new SineduUrls();

        services.AddDbContext<IdentityDbContext>(o =>
        {
            o.UseSineduPostgres(connectionString, IdentityDbContext.Schema);
            o.UseOpenIddict();
        });

        services.AddScoped<TotpService>();
        services.AddScoped<AccountService>();
        services.AddScoped<IAccountInvitations>(sp => sp.GetRequiredService<AccountService>());

        AddOpenIddict(services, urls, configuration);
        AddAuthentication(services, urls);

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.PlatformStaff, p => p
                .AddAuthenticationSchemes(AccountSchemes.PanelCookie)
                .RequireAuthenticatedUser()
                .RequireClaim(AccountClaims.Role, Roles.SuperAdmin, Roles.Support))
            .AddPolicy(Policies.PlatformSuperAdmin, p => p
                .AddAuthenticationSchemes(AccountSchemes.PanelCookie)
                .RequireAuthenticatedUser()
                .RequireClaim(AccountClaims.Role, Roles.SuperAdmin))
            .AddPolicy(InternalEndpoints.Policy, p => p
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => ctx.User.HasScope(OidcClients.MembershipsScope)));

        services.Configure<RateLimiterOptions>(o => o.AddPolicy(SensitiveRateLimit, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })));

        services.AddHostedService<OidcClientRegistrar>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AccountEndpoints.Map(endpoints);
        OidcEndpoints.Map(endpoints);
        InternalEndpoints.Map(endpoints);
        PanelSessionEndpoints.Map(endpoints);
        PlatformAccounts.Map(endpoints);
    }

    private static void AddOpenIddict(IServiceCollection services, SineduUrls urls, IConfiguration configuration)
    {
        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
            .AddServer(o =>
            {
                o.SetIssuer(urls.AccountUrl);
                o.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/endsession");
                o.AllowAuthorizationCodeFlow().AllowClientCredentialsFlow();
                o.RequireProofKeyForCodeExchange();
                o.RegisterScopes(OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.Profile, OidcClients.MembershipsScope);
                o.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                o.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                o.SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));

                // Produção: certificados persistentes (reiniciar não invalida tokens em trânsito). Dev/testes: chaves efêmeras.
                var signing = configuration["Oidc:SigningCertificatePath"];
                var encryption = configuration["Oidc:EncryptionCertificatePath"];
                if (!string.IsNullOrWhiteSpace(signing) && !string.IsNullOrWhiteSpace(encryption))
                {
                    var password = configuration["Oidc:CertificatePassword"];
                    o.AddSigningCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(signing, password));
                    o.AddEncryptionCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(encryption, password));
                }
                else if (urls.SecureCookies)
                {
                    throw new InvalidOperationException("Oidc:SigningCertificatePath e Oidc:EncryptionCertificatePath são obrigatórios fora do ambiente local.");
                }
                else
                {
                    o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
                }

                var aspnet = o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (!urls.SecureCookies)
                {
                    aspnet.DisableTransportSecurityRequirement();
                }

                o.RemoveEventHandler(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor);
                o.AddEventHandler(ValidateRedirectUriHandler.Descriptor);
                o.RemoveEventHandler(OpenIddictServerHandlers.Session.ValidateClientPostLogoutRedirectUri.Descriptor);
                o.AddEventHandler(ValidatePostLogoutRedirectUriHandler.Descriptor);
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });
    }

    private static void AddAuthentication(IServiceCollection services, SineduUrls urls)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = AccountSchemes.HostSelector;
                o.DefaultChallengeScheme = AccountSchemes.HostSelector;
            })
            .AddPolicyScheme(AccountSchemes.HostSelector, null, o => o.ForwardDefaultSelector = ctx =>
                ctx.RequestServices.GetRequiredService<HostClassifier>().Classify(ctx.Request.Host.Value).Kind == HostKind.Panel
                    ? AccountSchemes.PanelCookie
                    : AccountSchemes.Session)
            .AddCookie(AccountSchemes.Session, o =>
            {
                ConfigureCookie(o, urls, "sinedu-conta", TimeSpan.FromHours(10), sliding: false);
                o.Events.OnValidatePrincipal = ValidateAccountStampAsync;
            })
            .AddCookie(AccountSchemes.Pending, o => ConfigureCookie(o, urls, "sinedu-conta-etapa", TimeSpan.FromMinutes(10), sliding: false))
            .AddCookie(AccountSchemes.PanelCookie, o =>
            {
                ConfigureCookie(o, urls, "sinedu-painel", TimeSpan.FromMinutes(30), sliding: true);
                o.Events.OnValidatePrincipal = ValidatePanelSessionAsync;
            })
            .AddOpenIdConnect(AccountSchemes.PanelOidc, _ => { });

        services.AddOptions<OpenIdConnectOptions>(AccountSchemes.PanelOidc)
            .Configure<IOptions<SineduUrls>, IConfiguration>((o, urlOptions, configuration) =>
            {
                var u = urlOptions.Value;
                o.SignInScheme = AccountSchemes.PanelCookie;
                o.Authority = u.AccountUrl.ToString();
                o.ClientId = OidcClients.Panel;
                o.ClientSecret = configuration["Oidc:PanelClientSecret"];
                o.ResponseType = "code";
                o.ResponseMode = "query";
                o.UsePkce = true;
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.CallbackPath = OidcClients.CallbackPath;
                o.SignedOutCallbackPath = OidcClients.SignedOutPath;
                o.SignedOutRedirectUri = "/";
                o.RequireHttpsMetadata = u.SecureCookies;
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = u.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SecurePolicy = u.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.BackchannelHttpHandler = new AccountBackchannelHandler(urlOptions) { InnerHandler = new HttpClientHandler() };
                o.TokenValidationParameters.NameClaimType = AccountClaims.Name;
                o.TokenValidationParameters.RoleClaimType = AccountClaims.Role;
                o.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.HandleResponse();
                    }

                    return Task.CompletedTask;
                };
                o.Events.OnTokenValidated = AddPlatformRolesAsync;
            });
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o, SineduUrls urls, string name, TimeSpan lifetime, bool sliding)
    {
        o.Cookie.Name = urls.CookieName(name);
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = urls.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = lifetime;
        o.SlidingExpiration = sliding;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    /// <summary>Sessão do login único deixa de valer se a conta foi desativada ou trocou senha/MFA.</summary>
    private static async Task ValidateAccountStampAsync(CookieValidatePrincipalContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var userId = AccountClaims.GetUserId(ctx.Principal!);
        var stamp = ctx.Principal!.FindFirstValue(AccountClaims.Stamp);
        var valid = await db.Users.AnyAsync(u => u.Id == userId && u.Status == AccountStatus.Active && u.SecurityStamp.ToString() == stamp);
        if (!valid)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AccountSchemes.Session);
        }
    }

    private static async Task ValidatePanelSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var userId = AccountClaims.GetUserId(ctx.Principal!);
        var valid = await db.Users.AnyAsync(u => u.Id == userId && u.Status == AccountStatus.Active && u.Kind == AccountKind.Platform
            && u.PlatformRoles.Any(r => r.RevokedAt == null));
        if (!valid)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AccountSchemes.PanelCookie);
        }
    }

    private static async Task AddPlatformRolesAsync(TokenValidatedContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var userId = AccountClaims.GetUserId(ctx.Principal!);
        var roles = await db.PlatformRoles.Where(r => r.UserId == userId && r.RevokedAt == null).Select(r => r.Role).ToListAsync();
        var hasMfa = ctx.Principal!.FindAll(AccountClaims.Amr).Any(c => c.Value == AccountClaims.AmrMfa);
        if (roles.Count == 0 || !hasMfa)
        {
            ctx.Fail("Sem acesso ao painel.");
            return;
        }

        var identity = (ClaimsIdentity)ctx.Principal.Identity!;
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(AccountClaims.Role, role));
        }
    }
}

/// <summary>Login/logout e dados da sessão no painel.</summary>
internal static class PanelSessionEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", (string? returnUrl, HttpContext ctx) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                [AccountSchemes.PanelOidc]))
            .RequireSurface(endpoints, u => u.PanelUrl);

        endpoints.MapPost("/auth/logout", () =>
            Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [AccountSchemes.PanelCookie, AccountSchemes.PanelOidc]))
            .RequireSurface(endpoints, u => u.PanelUrl);

        endpoints.MapPanelApi("/me").MapGet("/", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue(AccountClaims.Subject),
            name = user.FindFirstValue(AccountClaims.Name),
            email = user.FindFirstValue(AccountClaims.Email),
            roles = user.FindAll(AccountClaims.Role).Select(c => c.Value),
        })).RequireAuthorization(Policies.PlatformStaff);
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";

}
