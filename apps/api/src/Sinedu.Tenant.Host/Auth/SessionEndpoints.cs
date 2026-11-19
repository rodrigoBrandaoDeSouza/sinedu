using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.Host.Auth;

internal static class SessionEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", async (string? returnUrl, bool? stepUp, HttpContext ctx) =>
        {
            var target = SafeReturnUrl(returnUrl);
            var current = await ctx.AuthenticateAsync(SchoolSchemes.Cookie);
            if (current.Succeeded && stepUp != true)
            {
                return Results.Redirect(target);
            }

            return Results.Challenge(SchoolAuthentication.LoginProperties(target, stepUp == true), [SchoolSchemes.Oidc]);
        }).AllowAnonymous();

        // POST de formulário (navegação), para o redirecionamento ao login único funcionar.
        endpoints.MapPost("/auth/logout", async (HttpContext ctx, AccessDbContext db, IAuditLog audit, TimeProvider clock) =>
        {
            var current = await ctx.AuthenticateAsync(SchoolSchemes.Cookie);
            if (current.Succeeded && Guid.TryParse(current.Principal!.FindFirstValue(SchoolClaims.Session), out var sessionId))
            {
                await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()));
                await audit.WriteAsync(new AuditEntry("logout"));
            }

            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [SchoolSchemes.Cookie, SchoolSchemes.Oidc]);
        }).AllowAnonymous();

        endpoints.MapGet("/api/me", async (ClaimsPrincipal user, TenantScope tenant, IOptions<SineduUrls> urls, AccessDbContext db, CancellationToken ct) =>
        {
            var roles = user.FindAll(SchoolClaims.Role).Select(c => c.Value).ToList();

            // Sessão de suporte (P-11): a SPA mostra até quando o acesso vale.
            DateTimeOffset? supportUntil = null;
            if (SupportSession.IsSupportOnly(roles) && Guid.TryParse(user.FindFirstValue(SchoolClaims.Subject), out var userId))
            {
                supportUntil = await db.Roles.AsNoTracking()
                    .Where(r => r.UserId == userId && r.Role == Roles.PlatformSupport && r.RevokedAt == null)
                    .MaxAsync(r => r.ExpiresAt, ct);
            }

            return Results.Ok(new
            {
                user = new
                {
                    id = user.FindFirstValue(SchoolClaims.Subject),
                    name = user.FindFirstValue(SchoolClaims.Name),
                    email = user.FindFirstValue(SchoolClaims.Email),
                },
                roles,
                school = new { slug = tenant.Current.Slug, name = tenant.Current.DisplayName },
                mfa = SchoolClaims.HasMfa(user),
                authTime = SchoolClaims.AuthTimeOf(user),
                accountUrl = urls.Value.AccountUrl,
                supportUntil,
            });
        });

        // Usado pela SPA e pelos testes para confirmar a autenticação forte recente antes de abrir áreas sensíveis.
        endpoints.MapGet("/api/security/step-up", () => Results.NoContent()).RequireAuthorization(Policies.RecentStrongAuth);
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
