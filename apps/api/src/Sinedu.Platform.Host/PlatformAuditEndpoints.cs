using Microsoft.EntityFrameworkCore;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Host;

/// <summary>
/// Auditoria da plataforma no painel (matriz: só SUPER_ADMIN). Ações do painel e do login único: quem, o quê,
/// quando, em qual escola e com qual resultado. A plataforma não tem dado de aluno para registrar.
/// </summary>
internal static class PlatformAuditEndpoints
{
    public const int PageSize = 50;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPanelApi("/audit").RequireAuthorization(Policies.PlatformSuperAdmin).MapGet("/", async (
            string? category, string? action, Guid? tenantId, DateOnly? from, DateOnly? to, int? page,
            AuditDbContext audit, IdentityDbContext identity, TenantsDbContext tenants, CancellationToken ct) =>
        {
            var query = audit.Events.AsNoTracking();
            query = category switch
            {
                "escolas" => query.Where(e => e.Action.StartsWith("tenant_") || e.Action == "metrics_collected"),
                "contas" => query.Where(e => e.Action.StartsWith("platform_") || e.Action.StartsWith("account_") || e.Action.StartsWith("mfa_")
                    || e.Action.StartsWith("login_") || e.Action.StartsWith("password_") || e.Action.StartsWith("sso_") || e.Action == "invitation_accepted"),
                "cobranca" => query.Where(e => e.Action.StartsWith("plan_") || e.Action.StartsWith("contract_") || e.Action.StartsWith("invoice")),
                "suporte" => query.Where(e => e.Action.StartsWith("support_")),
                _ => query,
            };
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(e => e.Action == action);
            if (tenantId is { } tid)
            {
                var id = tid.ToString();
                query = query.Where(e => e.ResourceId == id);
            }

            var brt = TimeSpan.FromHours(-3);
            if (from is { } f) query = query.Where(e => e.OccurredAt >= new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), brt));
            if (to is { } t) query = query.Where(e => e.OccurredAt < new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), brt));

            var current = Math.Max(page ?? 1, 1);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(e => e.Id).Skip((current - 1) * PageSize).Take(PageSize).ToListAsync(ct);

            var userIds = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
            var people = await identity.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.FullName, u.Email }, ct);
            var schools = await tenants.Tenants.AsNoTracking().ToDictionaryAsync(x => x.Id.ToString(), x => x.DisplayName, ct);

            return Results.Ok(new
            {
                total,
                page = current,
                pageSize = PageSize,
                items = rows.Select(e => new
                {
                    e.Id,
                    e.OccurredAt,
                    e.Action,
                    result = e.Result.ToString(),
                    user = e.UserId is { } u ? new { id = u, name = people.GetValueOrDefault(u)?.FullName ?? "—", email = people.GetValueOrDefault(u)?.Email } : null,
                    e.ResourceType,
                    e.ResourceId,
                    school = e.ResourceId is { } r && schools.TryGetValue(r, out var name) ? name : null,
                    e.Ip,
                }),
            });
        });
    }
}
