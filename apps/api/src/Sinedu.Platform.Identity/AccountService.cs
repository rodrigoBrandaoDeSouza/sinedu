using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sinedu.Platform.Identity.Security;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Identity;

public enum LoginOutcome
{
    InvalidCredentials,
    LockedOut,
    SignedIn,
    MfaRequired,
    MfaEnrollmentRequired,
}

public sealed record LoginResult(LoginOutcome Outcome, UserAccount? User = null);

/// <summary>Regras de conta: login, bloqueio, MFA, convites e redefinição de senha.</summary>
public sealed class AccountService(
    IdentityDbContext db,
    TotpService totp,
    IEmailSender emailSender,
    IAuditLog audit,
    IOptions<SineduUrls> urls,
    TimeProvider clock)
    : IAccountInvitations
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    // Hash fixo usado quando o e-mail não existe, para o tempo de resposta não revelar contas.
    private const string DummyHash = "argon2id$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public async Task<LoginResult> PasswordLoginAsync(string emailAddress, string password, CancellationToken ct)
    {
        var normalized = Normalize(emailAddress ?? "");
        var user = await db.Users.Include(u => u.Memberships).Include(u => u.PlatformRoles)
            .FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user is null || user.PasswordHash is null || user.Status != AccountStatus.Active)
        {
            await PasswordHasher.VerifyAsync(password ?? "", DummyHash);
            await audit.WriteAsync(new AuditEntry("login_failed", AuditResult.Denied, UserId: user?.Id), ct);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var now = clock.GetUtcNow();
        if (user.LockoutUntil is { } until && until > now)
        {
            await audit.WriteAsync(new AuditEntry("login_locked", AuditResult.Denied, UserId: user.Id), ct);
            return new LoginResult(LoginOutcome.LockedOut);
        }

        if (!await PasswordHasher.VerifyAsync(password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutUntil = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }

            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("login_failed", AuditResult.Denied, UserId: user.Id), ct);
            return new LoginResult(user.LockoutUntil > now ? LoginOutcome.LockedOut : LoginOutcome.InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await db.SaveChangesAsync(ct);

        if (user.MfaEnabled)
        {
            return new LoginResult(LoginOutcome.MfaRequired, user);
        }

        return RequiresMfa(user)
            ? new LoginResult(LoginOutcome.MfaEnrollmentRequired, user)
            : new LoginResult(LoginOutcome.SignedIn, user);
    }

    /// <summary>D6: MFA obrigatório para contas da plataforma e para quem tem papel privilegiado em alguma escola.</summary>
    public static bool RequiresMfa(UserAccount user) =>
        user.Kind == AccountKind.Platform || user.Memberships.Any(m => m.Active && m.Privileged);

    public async Task<bool> VerifySecondFactorAsync(UserAccount user, string code, CancellationToken ct)
    {
        var input = (code ?? "").Trim();
        if (input.Length > 6 && input.Any(char.IsLetter))
        {
            var hash = TokenHasher.Hash(input);
            var recovery = await db.RecoveryCodes.FirstOrDefaultAsync(r => r.UserId == user.Id && r.CodeHash == hash && r.UsedAt == null, ct);
            if (recovery is null)
            {
                await audit.WriteAsync(new AuditEntry("mfa_failed", AuditResult.Denied, UserId: user.Id), ct);
                return false;
            }

            recovery.UsedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("mfa_recovery_code_used", UserId: user.Id), ct);
            return true;
        }

        if (user.TotpSecretEncrypted is null)
        {
            return false;
        }

        var secret = await totp.UnprotectAsync(user.TotpSecretEncrypted, ct);
        var step = TotpService.Verify(secret, input, user.LastTotpStep);
        if (step is null)
        {
            await audit.WriteAsync(new AuditEntry("mfa_failed", AuditResult.Denied, UserId: user.Id), ct);
            return false;
        }

        user.LastTotpStep = step;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(string Secret, string OtpAuthUri)> BeginEnrollmentAsync(UserAccount user, CancellationToken ct)
    {
        var secret = TotpService.NewSecret();
        user.TotpSecretEncrypted = await totp.ProtectAsync(secret, ct);
        user.MfaEnabled = false;
        user.LastTotpStep = null;
        await db.SaveChangesAsync(ct);
        return (TotpService.ToBase32(secret), TotpService.OtpAuthUri(secret, user.Email));
    }

    public async Task<IReadOnlyList<string>?> CompleteEnrollmentAsync(UserAccount user, string code, CancellationToken ct)
    {
        if (user.TotpSecretEncrypted is null || !await VerifySecondFactorAsync(user, code, ct))
        {
            return null;
        }

        user.MfaEnabled = true;
        db.RecoveryCodes.RemoveRange(db.RecoveryCodes.Where(r => r.UserId == user.Id));
        var codes = TotpService.NewRecoveryCodes();
        db.RecoveryCodes.AddRange(codes.Select(c => new RecoveryCode { Id = Guid.CreateVersion7(), UserId = user.Id, CodeHash = TokenHasher.Hash(c) }));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("mfa_enabled", UserId: user.Id), ct);
        return codes;
    }

    public async Task MarkSignedInAsync(UserAccount user, CancellationToken ct)
    {
        user.LastLoginAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("login_succeeded", UserId: user.Id), ct);
    }

    public async Task<Guid> InviteToTenantAsync(
        Guid tenantId, string tenantSlug, string tenantName, string email, string fullName, bool privileged, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(email);
        var now = clock.GetUtcNow();
        var user = await db.Users.Include(u => u.Memberships).FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

        if (user is { Kind: AccountKind.Platform })
        {
            throw new InvalidOperationException("Contas da plataforma não podem receber papéis em escolas.");
        }

        var isNew = user is null;
        user ??= new UserAccount
        {
            Id = Guid.CreateVersion7(),
            Email = normalized,
            FullName = fullName.Trim(),
            Status = AccountStatus.Invited,
            Kind = AccountKind.School,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now,
        };
        if (isNew)
        {
            db.Users.Add(user);
        }

        var membership = user.Memberships.FirstOrDefault(m => m.TenantId == tenantId);
        if (membership is null)
        {
            membership = new TenantMembership
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                TenantId = tenantId,
                TenantSlug = tenantSlug,
                TenantName = tenantName,
                CreatedAt = now,
            };
            db.Memberships.Add(membership);
        }

        membership.Active = true;
        membership.Privileged |= privileged;
        membership.TenantName = tenantName;
        membership.TenantSlug = tenantSlug;
        membership.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        if (user.Status == AccountStatus.Invited)
        {
            var token = await IssueTokenAsync(user, AccountTokenPurpose.Invitation, InvitationLifetime, cancellationToken);
            var link = new Uri(urls.Value.AccountUrl, $"convite?token={token}");
            await emailSender.SendAsync(new EmailMessage(
                user.Email,
                $"Convite para acessar o Sinedu — {tenantName}",
                $"Olá, {user.FullName}.\n\nVocê foi convidado(a) para acessar o Sinedu na escola {tenantName}.\n\n" +
                $"Para criar sua senha, acesse:\n{link}\n\nO link vale por 7 dias. Se você não esperava este convite, ignore este e-mail.") { TenantId = tenantId }, cancellationToken);
        }
        else
        {
            await emailSender.SendAsync(new EmailMessage(
                user.Email,
                $"Novo acesso no Sinedu — {tenantName}",
                $"Olá, {user.FullName}.\n\nSua conta agora tem acesso à escola {tenantName}.\n\nEntre em {urls.Value.AccountUrl} para acessar.") { TenantId = tenantId }, cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("tenant_invitation_sent", UserId: user.Id, ResourceType: "tenant", ResourceId: tenantId.ToString()), cancellationToken);
        return user.Id;
    }

    /// <summary>
    /// Primeiro SUPER_ADMIN de uma instalação nova (comando <c>create-super-admin</c>). Só funciona enquanto não
    /// existe nenhum SUPER_ADMIN ativo: depois disso, contas da plataforma só por quem já é SUPER_ADMIN.
    /// A pessoa recebe convite por e-mail, cria a senha e cadastra o MFA no primeiro acesso.
    /// </summary>
    public async Task<Guid> InviteFirstSuperAdminAsync(string email, string fullName, CancellationToken ct)
    {
        if (await db.PlatformRoles.AnyAsync(r => r.Role == Roles.SuperAdmin && r.RevokedAt == null, ct))
        {
            throw new InvalidOperationException("Já existe um SUPER_ADMIN ativo nesta instalação.");
        }

        var normalized = Normalize(email);
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
        {
            throw new InvalidOperationException("Já existe uma conta com este e-mail.");
        }

        var now = clock.GetUtcNow();
        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(), Email = normalized, FullName = fullName.Trim(), Status = AccountStatus.Invited, Kind = AccountKind.Platform,
            SecurityStamp = Guid.NewGuid(), CreatedAt = now,
        };
        db.Users.Add(user);
        db.PlatformRoles.Add(new PlatformRoleGrant { Id = Guid.CreateVersion7(), UserId = user.Id, Role = Roles.SuperAdmin, GrantedAt = now });
        await db.SaveChangesAsync(ct);

        var token = await IssueTokenAsync(user, AccountTokenPurpose.Invitation, InvitationLifetime, ct);
        var link = new Uri(urls.Value.AccountUrl, $"convite?token={token}");
        await emailSender.SendAsync(new EmailMessage(
            user.Email,
            "Convite para administrar o Sinedu",
            $"Olá, {user.FullName}.\n\nVocê foi cadastrado(a) como administrador(a) da plataforma Sinedu.\n\n" +
            $"Para criar sua senha e cadastrar a verificação em duas etapas, acesse:\n{link}\n\nO link vale por 7 dias."), ct);
        await audit.WriteAsync(new AuditEntry("platform_first_admin_invited", UserId: user.Id), ct);
        return user.Id;
    }

    /// <summary>Nova conta da plataforma (SUPER_ADMIN ou SUPORTE), convidada por um SUPER_ADMIN. MFA no primeiro acesso.</summary>
    public async Task<Guid> InvitePlatformAccountAsync(string email, string fullName, string role, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(), Email = Normalize(email), FullName = fullName.Trim(), Status = AccountStatus.Invited, Kind = AccountKind.Platform,
            SecurityStamp = Guid.NewGuid(), CreatedAt = now,
        };
        db.Users.Add(user);
        db.PlatformRoles.Add(new PlatformRoleGrant { Id = Guid.CreateVersion7(), UserId = user.Id, Role = role, GrantedAt = now });
        await db.SaveChangesAsync(ct);

        var token = await IssueTokenAsync(user, AccountTokenPurpose.Invitation, InvitationLifetime, ct);
        var link = new Uri(urls.Value.AccountUrl, $"convite?token={token}");
        await emailSender.SendAsync(new EmailMessage(
            user.Email,
            "Convite para o painel do Sinedu",
            $"Olá, {user.FullName}.\n\nVocê foi convidado(a) para o painel da plataforma Sinedu ({(role == Roles.SuperAdmin ? "administração" : "suporte")}).\n\n" +
            $"Para criar sua senha e cadastrar a verificação em duas etapas, acesse:\n{link}\n\nO link vale por 7 dias."), ct);
        return user.Id;
    }

    public async Task SyncMembershipAsync(Guid tenantId, Guid userId, bool active, bool privileged, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
        if (membership is null)
        {
            return;
        }

        membership.Active = active;
        membership.Privileged = active && privileged;
        membership.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<(UserAccount User, AccountToken Token)?> FindTokenAsync(string token, AccountTokenPurpose purpose, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(token ?? "");
        var stored = await db.Tokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (stored is null || stored.UsedAt is not null || stored.ExpiresAt < clock.GetUtcNow())
        {
            return null;
        }

        var user = await db.Users.Include(u => u.Memberships).FirstAsync(u => u.Id == stored.UserId, ct);
        return (user, stored);
    }

    public async Task<UserAccount?> AcceptInvitationAsync(string token, string fullName, string password, CancellationToken ct)
    {
        var found = await FindTokenAsync(token, AccountTokenPurpose.Invitation, ct);
        if (found is not { } f || f.User.Status != AccountStatus.Invited)
        {
            return null;
        }

        f.User.FullName = string.IsNullOrWhiteSpace(fullName) ? f.User.FullName : fullName.Trim();
        f.User.PasswordHash = await PasswordHasher.HashAsync(password);
        f.User.Status = AccountStatus.Active;
        f.User.SecurityStamp = Guid.NewGuid();
        f.Token.UsedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("invitation_accepted", UserId: f.User.Id), ct);
        return f.User;
    }

    public async Task RequestPasswordResetAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = Normalize(emailAddress ?? "");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && u.Status == AccountStatus.Active, ct);
        if (user is null)
        {
            return; // Mesma resposta para e-mails inexistentes.
        }

        var token = await IssueTokenAsync(user, AccountTokenPurpose.PasswordReset, ResetLifetime, ct);
        var link = new Uri(urls.Value.AccountUrl, $"redefinir-senha?token={token}");
        await emailSender.SendAsync(new EmailMessage(
            user.Email,
            "Redefinição de senha — Sinedu",
            $"Olá, {user.FullName}.\n\nRecebemos um pedido para redefinir sua senha. Para continuar, acesse:\n{link}\n\n" +
            "O link vale por 30 minutos. Se não foi você, ignore este e-mail — sua senha continua a mesma."), ct);
        await audit.WriteAsync(new AuditEntry("password_reset_requested", UserId: user.Id), ct);
    }

    public async Task<bool> ResetPasswordAsync(string token, string password, CancellationToken ct)
    {
        var found = await FindTokenAsync(token, AccountTokenPurpose.PasswordReset, ct);
        if (found is not { } f)
        {
            return false;
        }

        f.User.PasswordHash = await PasswordHasher.HashAsync(password);
        f.User.SecurityStamp = Guid.NewGuid();
        f.User.FailedLoginCount = 0;
        f.User.LockoutUntil = null;
        f.Token.UsedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("password_reset", UserId: f.User.Id), ct);
        return true;
    }

    private async Task<string> IssueTokenAsync(UserAccount user, AccountTokenPurpose purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Tokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        var token = TokenHasher.NewToken();
        db.Tokens.Add(new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = TokenHasher.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        });
        await db.SaveChangesAsync(ct);
        return token;
    }
}
