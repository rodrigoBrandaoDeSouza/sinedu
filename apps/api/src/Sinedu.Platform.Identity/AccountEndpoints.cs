using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sinedu.Platform.Identity.Security;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Identity;

/// <summary>API do login único (conta.{domínio}/api/auth).</summary>
internal static class AccountEndpoints
{
    private const string StageMfa = "mfa";
    private const string StageEnroll = "enroll";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapAccountApi("/auth").AllowAnonymous();

        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/mfa/verify", VerifyMfaAsync);
        auth.MapPost("/mfa/setup", SetupMfaAsync);
        auth.MapPost("/mfa/enable", EnableMfaAsync);
        auth.MapGet("/session", SessionAsync);
        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(AccountSchemes.Session);
            await ctx.SignOutAsync(AccountSchemes.Pending);
            return Results.NoContent();
        });

        auth.MapPost("/password/forgot", async (ForgotRequest body, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.RequestPasswordResetAsync(body.Email, ct);
            return Results.Accepted();
        }).RequireRateLimiting(IdentityModule.SensitiveRateLimit);

        auth.MapPost("/password/reset", async (ResetRequest body, AccountService accounts, CancellationToken ct) =>
        {
            var found = await accounts.FindTokenAsync(body.Token, AccountTokenPurpose.PasswordReset, ct);
            if (found is null) return InvalidLink();
            if (PasswordPolicy.Validate(body.Password, found.Value.User.Email) is { } error) return Field("password", error);

            await accounts.ResetPasswordAsync(body.Token, body.Password, ct);
            return Results.NoContent();
        });

        auth.MapGet("/invitations/{token}", async (string token, AccountService accounts, CancellationToken ct) =>
        {
            var found = await accounts.FindTokenAsync(token, AccountTokenPurpose.Invitation, ct);
            return found is { } f && f.User.Status == AccountStatus.Invited
                ? Results.Ok(new { f.User.Email, f.User.FullName, Schools = f.User.Memberships.Where(m => m.Active).Select(m => m.TenantName) })
                : InvalidLink();
        });

        auth.MapPost("/invitations/accept", async (AcceptRequest body, HttpContext ctx, AccountService accounts, CancellationToken ct) =>
        {
            var found = await accounts.FindTokenAsync(body.Token, AccountTokenPurpose.Invitation, ct);
            if (found is null) return InvalidLink();
            if (PasswordPolicy.Validate(body.Password, found.Value.User.Email) is { } error) return Field("password", error);

            var user = await accounts.AcceptInvitationAsync(body.Token, body.FullName, body.Password, ct);
            if (user is null) return InvalidLink();

            // Quem tem papel privilegiado cadastra o MFA antes de entrar.
            if (AccountService.RequiresMfa(user))
            {
                await ctx.SignInAsync(AccountSchemes.Pending, AccountClaims.PendingPrincipal(user, StageEnroll));
                return Results.Ok(new StatusResponse("mfa_enrollment_required"));
            }

            await SignInSessionAsync(ctx, accounts, user, mfa: false, ct);
            return Results.Ok(new StatusResponse("signed_in"));
        });
    }

    private static async Task<IResult> LoginAsync(LoginRequest body, HttpContext ctx, AccountService accounts, CancellationToken ct)
    {
        var result = await accounts.PasswordLoginAsync(body.Email, body.Password, ct);
        switch (result.Outcome)
        {
            case LoginOutcome.SignedIn:
                await SignInSessionAsync(ctx, accounts, result.User!, mfa: false, ct);
                return Results.Ok(new StatusResponse("signed_in"));
            case LoginOutcome.MfaRequired:
                await ctx.SignInAsync(AccountSchemes.Pending, AccountClaims.PendingPrincipal(result.User!, StageMfa));
                return Results.Ok(new StatusResponse("mfa_required"));
            case LoginOutcome.MfaEnrollmentRequired:
                await ctx.SignInAsync(AccountSchemes.Pending, AccountClaims.PendingPrincipal(result.User!, StageEnroll));
                return Results.Ok(new StatusResponse("mfa_enrollment_required"));
            case LoginOutcome.LockedOut:
                return Problem("account_locked", "Muitas tentativas. Tente novamente em 15 minutos ou redefina sua senha.");
            default:
                return Problem("invalid_credentials", "E-mail ou senha incorretos.");
        }
    }

    private static async Task<IResult> VerifyMfaAsync(CodeRequest body, HttpContext ctx, AccountService accounts, IdentityDbContext db, CancellationToken ct)
    {
        var user = await PendingUserAsync(ctx, db, StageMfa, ct);
        if (user is null) return Problem("session_expired", "Sua sessão expirou. Entre novamente.");

        if (!await accounts.VerifySecondFactorAsync(user, body.Code, ct))
        {
            return Problem("invalid_code", "Código inválido. Confira o aplicativo autenticador.");
        }

        await ctx.SignOutAsync(AccountSchemes.Pending);
        await SignInSessionAsync(ctx, accounts, user, mfa: true, ct);
        return Results.Ok(new StatusResponse("signed_in"));
    }

    private static async Task<IResult> SetupMfaAsync(HttpContext ctx, AccountService accounts, IdentityDbContext db, CancellationToken ct)
    {
        var user = await PendingUserAsync(ctx, db, StageEnroll, ct);
        if (user is null) return Problem("session_expired", "Sua sessão expirou. Entre novamente.");

        var (secret, uri) = await accounts.BeginEnrollmentAsync(user, ct);
        return Results.Ok(new { secret, otpAuthUri = uri });
    }

    private static async Task<IResult> EnableMfaAsync(CodeRequest body, HttpContext ctx, AccountService accounts, IdentityDbContext db, CancellationToken ct)
    {
        var user = await PendingUserAsync(ctx, db, StageEnroll, ct);
        if (user is null) return Problem("session_expired", "Sua sessão expirou. Entre novamente.");

        var codes = await accounts.CompleteEnrollmentAsync(user, body.Code, ct);
        if (codes is null) return Problem("invalid_code", "Código inválido. Confira o aplicativo autenticador.");

        await ctx.SignOutAsync(AccountSchemes.Pending);
        await SignInSessionAsync(ctx, accounts, user, mfa: true, ct);
        return Results.Ok(new { status = "signed_in", recoveryCodes = codes });
    }

    private static async Task<IResult> SessionAsync(HttpContext ctx, IdentityDbContext db, IOptions<SineduUrls> urls, ISupportAccessDirectory support, CancellationToken ct)
    {
        var pending = await ctx.AuthenticateAsync(AccountSchemes.Pending);
        var session = await ctx.AuthenticateAsync(AccountSchemes.Session);
        if (!session.Succeeded || AccountClaims.GetUserId(session.Principal) is not { } userId)
        {
            return Results.Ok(new { authenticated = false, pendingStage = pending.Principal?.FindFirstValue(AccountClaims.Stage) });
        }

        var user = await db.Users.AsNoTracking().Include(u => u.Memberships).Include(u => u.PlatformRoles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Ok(new { authenticated = false });

        // Conta da plataforma: escolas com liberação de suporte vigente (D17), com o prazo.
        var grants = user.Kind == AccountKind.Platform ? await support.ActiveGrantsAsync(user.Id, ct) : [];

        return Results.Ok(new
        {
            authenticated = true,
            user = new { user.Id, user.Email, user.FullName, user.MfaEnabled, kind = user.Kind.ToString() },
            schools = user.Memberships.Where(m => m.Active).OrderBy(m => m.TenantName)
                .Select(m => new { slug = m.TenantSlug, name = m.TenantName, url = urls.Value.TenantUrl(m.TenantSlug), supportUntil = (DateTimeOffset?)null })
                .Concat(grants.Select(g => new { slug = g.TenantSlug, name = g.TenantName, url = urls.Value.TenantUrl(g.TenantSlug), supportUntil = (DateTimeOffset?)g.ExpiresAt })),
            panel = user.Kind == AccountKind.Platform && user.PlatformRoles.Any(r => r.RevokedAt == null) ? urls.Value.PanelUrl : null,
        });
    }

    private static async Task SignInSessionAsync(HttpContext ctx, AccountService accounts, UserAccount user, bool mfa, CancellationToken ct)
    {
        var clock = ctx.RequestServices.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        await ctx.SignInAsync(AccountSchemes.Session, AccountClaims.SessionPrincipal(user, clock.GetUtcNow(), mfa));
        await accounts.MarkSignedInAsync(user, ct);
    }

    private static async Task<UserAccount?> PendingUserAsync(HttpContext ctx, IdentityDbContext db, string stage, CancellationToken ct)
    {
        var pending = await ctx.AuthenticateAsync(AccountSchemes.Pending);
        if (!pending.Succeeded || pending.Principal.FindFirstValue(AccountClaims.Stage) != stage
            || AccountClaims.GetUserId(pending.Principal) is not { } userId)
        {
            return null;
        }

        var stamp = pending.Principal.FindFirstValue(AccountClaims.Stamp);
        var user = await db.Users.Include(u => u.Memberships).FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is { Status: AccountStatus.Active } && user.SecurityStamp.ToString() == stamp ? user : null;
    }

    private static IResult Problem(string code, string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult InvalidLink() => Problem("invalid_link", "Este link é inválido ou expirou.");

    private static IResult Field(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });
}

internal sealed record LoginRequest(string Email, string Password);

internal sealed record CodeRequest(string Code);

internal sealed record ForgotRequest(string Email);

internal sealed record ResetRequest(string Token, string Password);

internal sealed record AcceptRequest(string Token, string FullName, string Password);

internal sealed record StatusResponse(string Status);
