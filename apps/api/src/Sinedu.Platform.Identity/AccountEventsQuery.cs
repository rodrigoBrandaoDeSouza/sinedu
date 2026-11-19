using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Identity;

/// <summary>Ação da plataforma sobre a conta de alguém da escola (P-12), para a auditoria da escola. Sem conteúdo.</summary>
public sealed record AccountEvent(long Id, string Action, Guid UserId, DateTimeOffset OccurredAt);

/// <summary>
/// Desbloqueios e reinícios de MFA feitos no painel, das pessoas com vínculo nesta escola, dos últimos 30 dias.
/// A escola busca pela API interna e grava na própria auditoria (a plataforma nunca escreve no banco da escola).
/// </summary>
public sealed class AccountEventsQuery(IdentityDbContext db, AuditDbContext audit, ITenantDirectory tenants, TimeProvider clock)
{
    public static readonly string[] Actions = ["account_unlocked", "account_mfa_reset"];

    public async Task<IReadOnlyList<AccountEvent>?> ForTenantAsync(string tenantSlug, CancellationToken ct)
    {
        var tenant = await tenants.FindBySlugAsync(tenantSlug, ct);
        if (tenant is null) return null;

        var members = (await db.Memberships.AsNoTracking().Where(m => m.TenantId == tenant.Id).Select(m => m.UserId).ToListAsync(ct))
            .Select(id => id.ToString()).ToList();
        var since = clock.GetUtcNow().AddDays(-30);
        var events = await audit.Events.AsNoTracking()
            .Where(e => Actions.Contains(e.Action) && e.OccurredAt >= since && e.ResourceId != null && members.Contains(e.ResourceId))
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.Action, e.ResourceId, e.OccurredAt })
            .ToListAsync(ct);
        return events.Select(e => new AccountEvent(e.Id, e.Action, Guid.Parse(e.ResourceId!), e.OccurredAt)).ToList();
    }
}
