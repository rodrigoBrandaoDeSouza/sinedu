using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.Host.Auth;

public static class SchoolSchemes
{
    public const string Cookie = "escola";
    public const string Oidc = "escola.oidc";
}

public static class SchoolClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string Tenant = "tenant";
    public const string Session = "sid";
    public const string AuthTime = "auth_time";
    public const string Amr = "amr";

    public static bool HasMfa(ClaimsPrincipal user) => user.FindAll(Amr).Any(c => c.Value == "mfa");

    public static DateTimeOffset? AuthTimeOf(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
}

public static class SchoolAuthentication
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(10);
    private const string StepUpItem = "stepup";

    public static IServiceCollection AddSchoolAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = SchoolSchemes.Cookie;
                o.DefaultChallengeScheme = SchoolSchemes.Cookie;
            })
            .AddCookie(SchoolSchemes.Cookie, _ => { })
            .AddOpenIdConnect(SchoolSchemes.Oidc, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(SchoolSchemes.Cookie).Configure<IOptions<SineduUrls>>((o, urls) =>
        {
            o.Cookie.Name = urls.Value.CookieName("sinedu-escola");
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = urls.Value.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            o.SlidingExpiration = true;
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
            o.Events.OnValidatePrincipal = ValidateSessionAsync;
        });

        services.AddOptions<OpenIdConnectOptions>(SchoolSchemes.Oidc).Configure<IOptions<SineduUrls>, IConfiguration>((o, urls, configuration) =>
        {
            var u = urls.Value;
            o.SignInScheme = SchoolSchemes.Cookie;
            o.Authority = u.AccountUrl.ToString();
            o.ClientId = OidcContract.TenantClientId;
            o.ClientSecret = configuration["Oidc:TenantClientSecret"];
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
            o.CallbackPath = OidcContract.CallbackPath;
            o.SignedOutCallbackPath = OidcContract.SignedOutPath;
            o.SignedOutRedirectUri = "/";
            o.RequireHttpsMetadata = u.SecureCookies;
            o.CorrelationCookie.SameSite = SameSiteMode.Lax;
            o.NonceCookie.SameSite = SameSiteMode.Lax;
            o.CorrelationCookie.SecurePolicy = u.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.NonceCookie.SecurePolicy = u.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.BackchannelHttpHandler = new AccountBackchannelHandler(urls) { InnerHandler = new HttpClientHandler() };
            o.TokenValidationParameters.NameClaimType = SchoolClaims.Name;
            o.TokenValidationParameters.RoleClaimType = SchoolClaims.Role;
            o.Events.OnRedirectToIdentityProvider = ctx =>
            {
                if (ctx.Properties.Items.ContainsKey(StepUpItem))
                {
                    // Força nova autenticação (senha + TOTP) no login único.
                    ctx.ProtocolMessage.MaxAge = "60";
                }

                return Task.CompletedTask;
            };
            o.Events.OnTokenValidated = SignInToSchoolAsync;
            o.Events.OnRemoteFailure = ctx =>
            {
                ctx.Response.Redirect("/sem-acesso");
                ctx.HandleResponse();
                return Task.CompletedTask;
            };
        });

        services.AddSingleton<IAuthorizationHandler, RecentStrongAuthHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpResultHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(SchoolSchemes.Cookie).RequireAuthenticatedUser().RequireClaim(SchoolClaims.Role).Build())
            .AddPolicy(Policies.SchoolMember, p => p.RequireAuthenticatedUser().RequireClaim(SchoolClaims.Role))
            .AddPolicy(Policies.SchoolManager, p => p.RequireClaim(SchoolClaims.Role, Roles.Director))
            .AddPolicy(Policies.SchoolConfig, p => p.RequireClaim(SchoolClaims.Role, Roles.Director, Roles.TechAdmin))
            .AddPolicy(Policies.PedagogicalStaff, p => p.RequireClaim(SchoolClaims.Role, Roles.Director, Roles.Pedagogue))
            .AddPolicy(Policies.SchoolTeam, p => p.RequireClaim(SchoolClaims.Role, Roles.Director, Roles.Pedagogue, Roles.TechAdmin))
            .AddPolicy(Policies.ImportCommon, p => p.RequireClaim(SchoolClaims.Role, Roles.Director, Roles.TechAdmin))
            .AddPolicy(Policies.StudentReader, p => p.RequireClaim(SchoolClaims.Role, Roles.Director, Roles.Pedagogue, Roles.Teacher, Roles.ReadOnly, Roles.PlatformSupport))
            .AddPolicy(Policies.RecentStrongAuth, p => p
                .RequireClaim(SchoolClaims.Role, Roles.Director, Roles.Pedagogue)
                .AddRequirements(new RecentStrongAuthRequirement()))
            // Saúde e Proteção/Sigilo (D6): mesmos requisitos do step-up; a política é separada para poder evoluir sozinha.
            .AddPolicy(Policies.ProtectedAccess, p => p
                .RequireAuthenticatedUser()
                .RequireClaim(SchoolClaims.Role, Roles.Director, Roles.Pedagogue)
                .AddRequirements(new RecentStrongAuthRequirement()));

        return services;
    }

    public static AuthenticationProperties LoginProperties(string returnUrl, bool stepUp)
    {
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        if (stepUp)
        {
            properties.Items[StepUpItem] = "1";
        }

        return properties;
    }

    /// <summary>
    /// Retorno do login único: confere papéis NESTA escola, cria a sessão local e grava só o essencial no cookie.
    /// Papéis não ficam no cookie — são lidos do banco da escola a cada requisição.
    /// </summary>
    private static async Task SignInToSchoolAsync(TokenValidatedContext ctx)
    {
        var services = ctx.HttpContext.RequestServices;
        var tenant = services.GetRequiredService<TenantScope>().Current;
        var db = services.GetRequiredService<AccessDbContext>();
        var access = services.GetRequiredService<AccessService>();
        var audit = services.GetRequiredService<IAuditLog>();
        var clock = services.GetRequiredService<TimeProvider>();
        var token = ctx.Principal!;

        if (!Guid.TryParse(token.FindFirstValue(SchoolClaims.Subject), out var userId))
        {
            ctx.Fail("Token sem identificador.");
            return;
        }

        var roles = await access.ActiveRolesAsync(userId, ctx.HttpContext.RequestAborted);
        if (roles.Count == 0 || (roles.Any(Roles.Privileged.Contains) && !SchoolClaims.HasMfa(token)))
        {
            await audit.WriteAsync(new AuditEntry("login_denied", AuditResult.Denied, UserId: userId));
            ctx.Fail("Sem acesso a esta escola.");
            return;
        }

        var now = clock.GetUtcNow();
        var member = await db.Members.FirstAsync(m => m.UserId == userId);
        member.DisplayName = token.FindFirstValue(SchoolClaims.Name) ?? member.DisplayName;
        member.Email = token.FindFirstValue(SchoolClaims.Email) ?? member.Email;
        member.LastSeenAt = now;

        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = now,
            LastSeenAt = now,
            AuthTime = SchoolClaims.AuthTimeOf(token) ?? now,
            ExpiresAt = now.Add(SessionLifetime),
            Ip = ctx.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(ctx.Request.Headers.UserAgent.ToString(), 300),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(SchoolClaims.Subject, userId.ToString()),
            new(SchoolClaims.Name, member.DisplayName),
            new(SchoolClaims.Email, member.Email),
            new(SchoolClaims.Tenant, tenant.Slug),
            new(SchoolClaims.Session, session.Id.ToString()),
            new(SchoolClaims.AuthTime, session.AuthTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };
        claims.AddRange(token.FindAll(SchoolClaims.Amr).Select(c => new Claim(SchoolClaims.Amr, c.Value)));

        ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchoolSchemes.Cookie, SchoolClaims.Name, SchoolClaims.Role));
        ctx.Properties!.IsPersistent = false;
        await audit.WriteAsync(new AuditEntry("login_succeeded", UserId: userId));
    }

    /// <summary>
    /// A cada requisição: o cookie precisa ser desta escola, a sessão não pode estar revogada/expirada e a
    /// pessoa precisa ter papel ativo. Papéis atuais são anexados ao principal (não persistidos no cookie).
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var services = ctx.HttpContext.RequestServices;
        var principal = ctx.Principal!;
        var scope = services.GetRequiredService<TenantScope>();

        if (!scope.HasTenant || principal.FindFirstValue(SchoolClaims.Tenant) != scope.Current.Slug
            || !Guid.TryParse(principal.FindFirstValue(SchoolClaims.Session), out var sessionId)
            || !Guid.TryParse(principal.FindFirstValue(SchoolClaims.Subject), out var userId))
        {
            ctx.RejectPrincipal();
            return;
        }

        var db = services.GetRequiredService<AccessDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(SchoolSchemes.Cookie);
            return;
        }

        var roles = await services.GetRequiredService<AccessService>().ActiveRolesAsync(userId, ctx.HttpContext.RequestAborted);
        if (roles.Count == 0)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(SchoolSchemes.Cookie);
            return;
        }

        if (now - session.LastSeenAt > TimeSpan.FromMinutes(5))
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => c.Type != SchoolClaims.Role && c.Type != AuditLog.SupportSessionClaim), SchoolSchemes.Cookie, SchoolClaims.Name, SchoolClaims.Role);
        identity.AddClaims(roles.Select(r => new Claim(SchoolClaims.Role, r)));

        // Sessão de suporte (P-11): marca toda auditoria desta requisição e ativa o modo somente leitura.
        if (SupportSession.IsSupportOnly(roles))
        {
            identity.AddClaim(new Claim(AuditLog.SupportSessionClaim, "true"));
        }
        ctx.ReplacePrincipal(new ClaimsPrincipal(identity));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public sealed class RecentStrongAuthRequirement : IAuthorizationRequirement;

/// <summary>Autenticação forte (senha + TOTP) nos últimos 15 minutos.</summary>
internal sealed class RecentStrongAuthHandler(TimeProvider clock) : AuthorizationHandler<RecentStrongAuthRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RecentStrongAuthRequirement requirement)
    {
        if (SchoolClaims.HasMfa(context.User)
            && SchoolClaims.AuthTimeOf(context.User) is { } authTime
            && clock.GetUtcNow() - authTime <= OidcContract.StepUpWindow)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Falha só por falta de step-up → 401 com código, para a SPA pedir reautenticação.</summary>
internal sealed class StepUpResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var isProtected = context.GetEndpoint()?.Metadata.GetMetadata<ProtectedResourceMetadata>() is not null;
        if (isProtected && (authorizeResult.Forbidden || authorizeResult.Challenged))
        {
            // Auditoria sem conteúdo: quem, qual aluno (da rota) e o resultado.
            var studentId = Guid.TryParse(context.GetRouteValue("studentId") as string, out var sid) ? sid : (Guid?)null;
            var audit = context.RequestServices.GetService<IAuditLog>();
            if (audit is not null && context.RequestServices.GetRequiredService<TenantScope>().HasTenant)
            {
                await audit.WriteAsync(new AuditEntry("protected_access_denied", AuditResult.Denied, "protected", StudentId: studentId), context.RequestAborted);
            }
        }

        var onlyStepUpFailed = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.All(r => r is RecentStrongAuthRequirement) == true
            && authorizeResult.AuthorizationFailure.FailedRequirements.Any();

        if (onlyStepUpFailed)
        {
            await Results.Problem(
                title: "Confirme sua identidade para continuar.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "step_up_required" }).ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
