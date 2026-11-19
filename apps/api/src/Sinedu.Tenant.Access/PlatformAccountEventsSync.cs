using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.Access;

/// <summary>
/// P-12: desbloqueios e reinícios de MFA feitos pela plataforma sobre contas desta escola entram também na auditoria
/// da escola (com o horário original e sem conteúdo). Idempotente: cada evento da plataforma entra uma vez só.
/// </summary>
public sealed class PlatformAccountEventsSync(IPlatformMemberships platform, AccessDbContext access, AuditDbContext audit, TenantScope tenant)
{
    public const string ResourceType = "platform_event";

    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var events = await platform.AccountEventsAsync(tenant.Current.Slug, ct);
        if (events.Count == 0) return 0;

        var ids = events.Select(e => e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var known = (await audit.Events.AsNoTracking().Where(e => e.ResourceType == ResourceType && ids.Contains(e.ResourceId!)).Select(e => e.ResourceId!).ToListAsync(ct)).ToHashSet();
        var userIds = events.Select(e => e.UserId).Distinct().ToList();
        var members = (await access.Members.AsNoTracking().Where(m => userIds.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct)).ToHashSet();

        var added = 0;
        foreach (var e in events)
        {
            var id = e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (known.Contains(id) || !members.Contains(e.UserId)) continue;
            audit.Events.Add(new AuditEvent
            {
                OccurredAt = e.OccurredAt,
                UserId = e.UserId,
                Action = e.Action == "account_mfa_reset" ? "platform_mfa_reset" : "platform_account_unlocked",
                ResourceType = ResourceType,
                ResourceId = id,
                Result = AuditResult.Allowed,
            });
            added++;
        }

        await audit.SaveChangesAsync(ct);
        return added;
    }
}
