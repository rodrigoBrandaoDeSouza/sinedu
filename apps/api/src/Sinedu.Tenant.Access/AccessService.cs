using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Access;

public enum AccessChangeResult
{
    Done,
    Forbidden,
    NotFound,
    LastDirector,
    PlatformAccount,
    Invalid,
}

/// <summary>
/// Regras de concessão de papéis (matriz §3 e salvaguardas §4):
/// só DIRETOR concede DIRETOR, PEDAGOGO e ADMIN_TECNICO; DIRETOR e ADMIN_TECNICO concedem PROFESSOR e CONSULTA;
/// toda concessão de DIRETOR/PEDAGOGO avisa todos os diretores; a escola nunca fica sem Diretor.
/// </summary>
public sealed class AccessService(
    AccessDbContext db,
    TenantScope tenant,
    IPlatformMemberships platform,
    IEmailSender email,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly HashSet<string> GrantableByDirectorOnly = [Roles.Director, Roles.Pedagogue, Roles.TechAdmin];
    private static readonly HashSet<string> GrantableBySchoolConfig = [Roles.Teacher, Roles.ReadOnly];

    public async Task<IReadOnlyList<string>> ActiveRolesAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.UserId == userId && r.RevokedAt == null && (r.ExpiresAt == null || r.ExpiresAt > now))
            .Select(r => r.Role)
            .ToListAsync(ct);
        var active = await db.Members.AnyAsync(m => m.UserId == userId && m.Status == MemberStatus.Active, ct);
        return active ? roles : [];
    }

    public static bool CanGrant(IReadOnlyCollection<string> actorRoles, string role) =>
        GrantableByDirectorOnly.Contains(role)
            ? actorRoles.Contains(Roles.Director)
            : GrantableBySchoolConfig.Contains(role) && (actorRoles.Contains(Roles.Director) || actorRoles.Contains(Roles.TechAdmin));

    public async Task<(AccessChangeResult Result, Guid? UserId)> InviteAsync(
        Guid actorId, IReadOnlyCollection<string> actorRoles, string emailAddress, string fullName, string role, CancellationToken ct)
    {
        if (!Roles.SchoolRoles.Contains(role) || role == Roles.PlatformSupport)
        {
            return (AccessChangeResult.Invalid, null);
        }

        if (!CanGrant(actorRoles, role))
        {
            await audit.WriteAsync(new AuditEntry("role_grant_denied", AuditResult.Denied, "role", role), ct);
            return (AccessChangeResult.Forbidden, null);
        }

        var privileged = Roles.Privileged.Contains(role);
        var invite = await platform.InviteAsync(tenant.Current.Slug, emailAddress, fullName, privileged, ct);
        if (invite.PlatformAccount || invite.UserId is not { } userId)
        {
            return (AccessChangeResult.PlatformAccount, null);
        }

        var now = clock.GetUtcNow();
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, ct);
        if (member is null)
        {
            db.Members.Add(member = new Member
            {
                UserId = userId,
                Email = emailAddress.Trim().ToLowerInvariant(),
                DisplayName = fullName.Trim(),
                Status = MemberStatus.Active,
                CreatedAt = now,
            });
        }

        member.Status = MemberStatus.Active;
        if (!await db.Roles.AnyAsync(r => r.UserId == userId && r.Role == role && r.RevokedAt == null, ct))
        {
            db.Roles.Add(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, Role = role, GrantedBy = actorId, GrantedAt = now });
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("role_granted", ResourceType: "role", ResourceId: role, UserId: actorId), ct);

        if (role is Roles.Director or Roles.Pedagogue)
        {
            await NotifyDirectorsAsync($"O papel {RoleLabel(role)} foi concedido a {member.DisplayName} ({member.Email}).", ct);
        }

        return (AccessChangeResult.Done, userId);
    }

    public async Task<AccessChangeResult> RevokeAsync(Guid actorId, IReadOnlyCollection<string> actorRoles, Guid userId, string role, CancellationToken ct)
    {
        if (!CanGrant(actorRoles, role))
        {
            await audit.WriteAsync(new AuditEntry("role_revoke_denied", AuditResult.Denied, "role", role), ct);
            return AccessChangeResult.Forbidden;
        }

        var now = clock.GetUtcNow();
        var grant = await db.Roles.FirstOrDefaultAsync(r => r.UserId == userId && r.Role == role && r.RevokedAt == null, ct);
        if (grant is null)
        {
            return AccessChangeResult.NotFound;
        }

        if (role == Roles.Director && await db.Roles.CountAsync(r => r.Role == Roles.Director && r.RevokedAt == null, ct) <= 1)
        {
            return AccessChangeResult.LastDirector;
        }

        grant.RevokedAt = now;
        grant.RevokedBy = actorId;

        // Sem papéis restantes, as sessões da pessoa nesta escola caem imediatamente.
        var remaining = await db.Roles.Where(r => r.UserId == userId && r.RevokedAt == null && r.Id != grant.Id).Select(r => r.Role).ToListAsync(ct);
        if (remaining.Count == 0)
        {
            await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }

        await db.SaveChangesAsync(ct);
        await platform.SyncAsync(tenant.Current.Slug, userId, active: remaining.Count > 0, privileged: remaining.Any(Roles.Privileged.Contains), ct);
        await audit.WriteAsync(new AuditEntry("role_revoked", ResourceType: "role", ResourceId: role, UserId: actorId), ct);
        return AccessChangeResult.Done;
    }

    private async Task NotifyDirectorsAsync(string text, CancellationToken ct)
    {
        var directors = await db.Members.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active && m.Roles.Any(r => r.Role == Roles.Director && r.RevokedAt == null))
            .Select(m => m.Email)
            .ToListAsync(ct);

        foreach (var to in directors)
        {
            await email.SendAsync(new EmailMessage(
                to,
                $"Alteração de acesso — {tenant.Current.DisplayName}",
                $"{text}\n\nEste papel dá acesso a dados de Saúde e Proteção dos alunos. Se você não reconhece esta alteração, " +
                "revogue o acesso em Usuários e acessos e avise a equipe da plataforma."), ct);
        }
    }

    public static string RoleLabel(string role) => role switch
    {
        Roles.Director => "Diretor(a)",
        Roles.Pedagogue => "Pedagogo(a)",
        Roles.TechAdmin => "Administrador(a) técnico(a)",
        Roles.Teacher => "Professor(a)",
        Roles.ReadOnly => "Consulta",
        Roles.PlatformSupport => "Suporte da plataforma",
        _ => role,
    };
}
