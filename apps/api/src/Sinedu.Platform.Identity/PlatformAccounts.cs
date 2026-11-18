using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Identity;

public sealed record InvitePlatformRequest(string Email, string FullName, string Role);

public sealed record ResetMfaRequest(string? Reason);

/// <summary>
/// P-12 — contas da plataforma e suporte a contas de usuários: convidar SUPER_ADMIN/SUPORTE, retirar acesso,
/// desbloquear e resetar MFA. Só SUPER_ADMIN. Toda ação avisa a pessoa por e-mail e fica na auditoria da plataforma.
/// Nenhuma ação aqui dá acesso a dados de escola (contas da plataforma não têm papel em escolas — D19).
/// </summary>
internal static class PlatformAccounts
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapPanelApi("/accounts").RequireAuthorization(Policies.PlatformSuperAdmin);

        api.MapGet("/", async (IdentityDbContext db, CancellationToken ct) =>
            (await db.Users.AsNoTracking().Include(u => u.PlatformRoles).Where(u => u.Kind == AccountKind.Platform).OrderBy(u => u.FullName).ToListAsync(ct))
                .Select(u => new
                {
                    u.Id, u.Email, u.FullName, Status = u.Status.ToString(), u.MfaEnabled, u.LockoutUntil, u.LastLoginAt,
                    Roles = u.PlatformRoles.Where(r => r.RevokedAt == null).Select(r => r.Role).ToList(),
                }));

        api.MapPost("/", async (InvitePlatformRequest body, IdentityDbContext db, AccountService accounts, IAuditLog audit, CancellationToken ct) =>
        {
            if (body.Role is not (Roles.SuperAdmin or Roles.Support))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Papel da plataforma inválido."] });
            }

            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || string.IsNullOrWhiteSpace(body.FullName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Informe nome e e-mail válidos."] });
            }

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                // D19: quem já tem conta (de escola ou da plataforma) usa outra conta para a plataforma.
                return Results.Problem(title: "Já existe uma conta com este e-mail. Contas da plataforma são separadas das contas de escola.", statusCode: 409);
            }

            var id = await accounts.InvitePlatformAccountAsync(email, body.FullName, body.Role, ct);
            await audit.WriteAsync(new AuditEntry("platform_account_invited", ResourceType: "account", ResourceId: id.ToString()), ct);
            return Results.Created($"/api/accounts/{id}", new { id });
        });

        api.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, IdentityDbContext db, IEmailSender email, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var target = await db.Users.Include(u => u.PlatformRoles).FirstOrDefaultAsync(u => u.Id == id && u.Kind == AccountKind.Platform, ct);
            if (target is null) return Results.NotFound();
            if (target.Id == AccountClaims.GetUserId(user)) return Results.Problem(title: "Você não pode retirar o próprio acesso.", statusCode: 409);

            var removingAdmin = target.PlatformRoles.Any(r => r.RevokedAt == null && r.Role == Roles.SuperAdmin);
            var admins = await db.PlatformRoles.CountAsync(r => r.RevokedAt == null && r.Role == Roles.SuperAdmin, ct);
            if (removingAdmin && admins <= 1) return Results.Problem(title: "A plataforma precisa de pelo menos um SUPER_ADMIN.", statusCode: 409);

            var now = clock.GetUtcNow();
            foreach (var role in target.PlatformRoles.Where(r => r.RevokedAt == null)) role.RevokedAt = now;
            target.SecurityStamp = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await email.SendAsync(new EmailMessage(target.Email, "Seu acesso ao painel do Sinedu foi retirado",
                $"Olá, {target.FullName}.\n\nSeu acesso ao painel da plataforma Sinedu foi retirado. Se não esperava isso, fale com a administração da plataforma."), ct);
            await audit.WriteAsync(new AuditEntry("platform_account_revoked", ResourceType: "account", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        });

        // Suporte a qualquer conta (escola ou plataforma): só identificação de equipe — nunca dados de aluno.
        api.MapGet("/lookup", async (string email, IdentityDbContext db, CancellationToken ct) =>
        {
            var normalized = (email ?? "").Trim().ToLowerInvariant();
            var u = await db.Users.AsNoTracking().Include(x => x.Memberships).FirstOrDefaultAsync(x => x.Email == normalized, ct);
            return u is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    u.Id, u.Email, u.FullName, Kind = u.Kind.ToString(), Status = u.Status.ToString(), u.MfaEnabled,
                    Locked = u.LockoutUntil > DateTimeOffset.UtcNow, u.LockoutUntil, u.LastLoginAt,
                    Schools = u.Memberships.Where(m => m.Active).Select(m => m.TenantName).ToList(),
                });
        });

        api.MapPost("/users/{id:guid}/unlock", async (Guid id, IdentityDbContext db, IEmailSender email, IAuditLog audit, CancellationToken ct) =>
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Results.NotFound();
            u.FailedLoginCount = 0;
            u.LockoutUntil = null;
            await db.SaveChangesAsync(ct);
            await email.SendAsync(new EmailMessage(u.Email, "Sua conta do Sinedu foi desbloqueada",
                $"Olá, {u.FullName}.\n\nSua conta foi desbloqueada pela administração da plataforma. Se você não pediu isso, troque sua senha."), ct);
            await audit.WriteAsync(new AuditEntry("account_unlocked", ResourceType: "account", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        });

        api.MapPost("/users/{id:guid}/reset-mfa", async (Guid id, ResetMfaRequest body, ClaimsPrincipal user, IdentityDbContext db, IEmailSender email, IAuditLog audit, CancellationToken ct) =>
        {
            if ((body.Reason?.Trim().Length ?? 0) < 10)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Explique o motivo (ex.: pessoa perdeu o celular e confirmou identidade por telefone)."] });
            }

            if (id == AccountClaims.GetUserId(user)) return Results.Problem(title: "Use os códigos de recuperação para o seu próprio MFA.", statusCode: 409);
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Results.NotFound();

            u.MfaEnabled = false;
            u.TotpSecretEncrypted = null;
            u.LastTotpStep = null;
            u.SecurityStamp = Guid.NewGuid(); // derruba as sessões abertas
            await db.RecoveryCodes.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            await email.SendAsync(new EmailMessage(u.Email, "A verificação em duas etapas da sua conta foi reiniciada",
                $"Olá, {u.FullName}.\n\nA administração da plataforma reiniciou a verificação em duas etapas da sua conta Sinedu. " +
                "No próximo acesso, você vai cadastrar o aplicativo autenticador de novo.\n\nSe você não pediu isso, avise a direção da sua escola imediatamente."), ct);
            await audit.WriteAsync(new AuditEntry("account_mfa_reset", ResourceType: "account", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        });
    }
}
