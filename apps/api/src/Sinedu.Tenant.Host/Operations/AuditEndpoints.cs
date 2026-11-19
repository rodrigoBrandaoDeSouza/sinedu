using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Auditoria da escola na tela (matriz: "Auditoria de acessos ao registro"; P9: só o Diretor).
/// Mostra quem fez o quê, quando e com qual resultado — os eventos nunca guardam conteúdo.
/// </summary>
internal static class AuditEndpoints
{
    public const int PageSize = 50;

    /// <summary>Saúde e Proteção/Sigilo: visualização, alteração, anexos, exportação e negativas.</summary>
    private static readonly string[] ProtectedActions =
    [
        "health_viewed", "protection_viewed", "protection_created", "protection_updated", "protection_revoked", "protected_exported",
        "protected_attachment_downloaded", "protected_attachment_uploaded", "protected_attachment_removed", "protected_access_denied",
        "protected_pending_viewed", "protected_keys_rewrapped",
    ];

    private static readonly string[] AccessActions =
    [
        "login_succeeded", "login_denied", "logout", "role_granted", "role_revoked", "role_grant_denied", "role_revoke_denied",
        "readonly_grants_changed", "student_access_denied", "support_approved", "support_denied", "support_revoked",
        "platform_mfa_reset", "platform_account_unlocked",
    ];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", async (string? category, Guid? studentId, Guid? userId, DateOnly? from, DateOnly? to, int? page,
            AuditDbContext audit, AccessDbContext access, StudentsDbContext students, CancellationToken ct) =>
        {
            var query = audit.Events.AsNoTracking();
            query = category switch
            {
                "protegidos" => query.Where(e => ProtectedActions.Contains(e.Action)),
                "acessos" => query.Where(e => AccessActions.Contains(e.Action)),
                "suporte" => query.Where(e => e.IsSupportSession || e.Action.StartsWith("support_")),
                _ => query,
            };
            if (studentId is { } sid) query = query.Where(e => e.StudentId == sid);
            if (userId is { } uid) query = query.Where(e => e.UserId == uid);
            var brt = TimeSpan.FromHours(-3);
            if (from is { } f) query = query.Where(e => e.OccurredAt >= new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), brt));
            if (to is { } t) query = query.Where(e => e.OccurredAt < new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), brt));

            var current = Math.Max(page ?? 1, 1);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(e => e.Id).Skip((current - 1) * PageSize).Take(PageSize).ToListAsync(ct);

            var userIds = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
            var people = await access.Members.AsNoTracking().Where(m => userIds.Contains(m.UserId))
                .ToDictionaryAsync(m => m.UserId, m => new { m.DisplayName, m.Email }, ct);
            var studentIds = rows.Where(r => r.StudentId != null).Select(r => r.StudentId!.Value).Distinct().ToList();
            var names = await students.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.SocialName ?? s.FullName, ct);

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
                    user = e.UserId is { } u ? new { id = u, name = people.GetValueOrDefault(u)?.DisplayName ?? "Conta removida", email = people.GetValueOrDefault(u)?.Email } : null,
                    student = e.StudentId is { } s ? new { id = s, name = names.GetValueOrDefault(s) ?? "—" } : null,
                    e.IsSupportSession,
                    e.Ip,
                }),
            });
        }).RequireAuthorization(Policies.SchoolManager);
    }
}
