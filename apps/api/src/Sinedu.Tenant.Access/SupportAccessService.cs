using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Access;

/// <summary>Liberação de suporte com o que o suporte fez durante ela (só contagens e horários, nunca conteúdo).</summary>
public sealed record SupportGrantView(
    Guid Id, string RequesterName, string RequesterEmail, string Reason, int DurationHours, string Status,
    DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecidedByName, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt,
    int Actions, DateTimeOffset? LastActionAt);

/// <summary>
/// Acesso de suporte do lado da escola (P-11, D17): só o Diretor aprova, recusa ou revoga. A aprovação cria o papel
/// SUPORTE_PLATAFORMA com prazo (checado a cada requisição); a revogação derruba as sessões na hora.
/// </summary>
public sealed partial class SupportAccessService(
    AccessDbContext db,
    AuditDbContext auditDb,
    TenantScope tenant,
    IPlatformMemberships platform,
    IEmailSender email,
    IAuditLog audit,
    IOptions<SineduUrls> urls,
    TimeProvider clock,
    ILogger<SupportAccessService> logger)
{
    public const string SupportAction = "support_session";

    /// <summary>Traz da plataforma os pedidos desta escola e avisa os diretores dos novos. Idempotente.</summary>
    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var remote = await platform.SupportRequestsAsync(tenant.Current.Slug, ct);
        var ids = remote.Select(r => r.Id).ToList();
        var local = await db.SupportGrants.Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        foreach (var r in remote)
        {
            if (!local.TryGetValue(r.Id, out var grant))
            {
                // Só pedidos ainda pendentes entram pela sincronização; decisões nascem aqui na escola.
                if (r.Status != nameof(SupportGrantStatus.Pending))
                {
                    continue;
                }

                db.SupportGrants.Add(new SupportAccessGrant
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    RequesterName = Truncate(r.RequesterName, 200),
                    RequesterEmail = Truncate(r.RequesterEmail, 254),
                    Reason = Truncate(r.Reason, 500),
                    DurationHours = Math.Clamp(r.DurationHours, 1, 24),
                    Status = SupportGrantStatus.Pending,
                    RequestedAt = r.CreatedAt,
                });
            }
            else if (grant.Status == SupportGrantStatus.Pending && r.Status == nameof(SupportGrantStatus.Cancelled))
            {
                grant.Status = SupportGrantStatus.Cancelled;
            }
        }

        await db.SaveChangesAsync(ct);

        var toNotify = await db.SupportGrants.Where(g => g.Status == SupportGrantStatus.Pending && g.NotifiedAt == null).ToListAsync(ct);
        foreach (var grant in toNotify)
        {
            await NotifyDirectorsAsync(
                $"Pedido de acesso de suporte — {tenant.Current.DisplayName}",
                $"{grant.RequesterName} ({grant.RequesterEmail}), da equipe da plataforma Sinedu, pediu acesso de suporte à escola por " +
                $"{grant.DurationHours} hora(s).\n\nMotivo informado: {grant.Reason}\n\n" +
                "O acesso é somente leitura de dados comuns (turmas e cadastro de alunos). Ocorrências, Saúde e Proteção/Sigilo nunca ficam visíveis. " +
                $"Só um(a) Diretor(a) pode aprovar ou recusar:\n{SupportPageUrl()}", ct);
            grant.NotifiedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return toNotify.Count;
    }

    /// <summary>Lista para a tela (Diretor e Admin técnico). Tenta sincronizar antes; se a plataforma não responde, mostra o que já tem.</summary>
    public async Task<(IReadOnlyList<SupportGrantView> Grants, bool Synced)> ListAsync(CancellationToken ct)
    {
        var synced = true;
        try
        {
            await SyncAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            synced = false;
            LogSyncFailed(logger, tenant.Current.Slug, ex.GetType().Name);
        }

        var now = clock.GetUtcNow();
        var grants = await db.SupportGrants.AsNoTracking().OrderByDescending(g => g.RequestedAt).Take(100).ToListAsync(ct);
        var deciders = await db.Members.AsNoTracking()
            .Where(m => grants.Select(g => g.DecidedBy).Contains(m.UserId))
            .ToDictionaryAsync(m => m.UserId, m => m.DisplayName, ct);
        var supportUsers = grants.Where(g => g.DecidedAt != null).Select(g => g.UserId).Distinct().ToList();
        var actions = await auditDb.Events.AsNoTracking()
            .Where(e => e.Action == SupportAction && e.UserId != null && supportUsers.Contains(e.UserId.Value))
            .Select(e => new { e.UserId, e.OccurredAt })
            .ToListAsync(ct);

        var views = grants.Select(g =>
        {
            var end = g.RevokedAt ?? (g.ExpiresAt < now ? g.ExpiresAt : now);
            var during = g.DecidedAt is null || g.Status is not (SupportGrantStatus.Approved or SupportGrantStatus.Revoked)
                ? []
                : actions.Where(a => a.UserId == g.UserId && a.OccurredAt >= g.DecidedAt && a.OccurredAt <= end).Select(a => a.OccurredAt).ToList();
            var status = g.Status == SupportGrantStatus.Approved && g.ExpiresAt <= now ? "Expired" : g.Status.ToString();
            return new SupportGrantView(
                g.Id, g.RequesterName, g.RequesterEmail, g.Reason, g.DurationHours, status, g.RequestedAt, g.DecidedAt,
                g.DecidedBy is { } d ? deciders.GetValueOrDefault(d) : null, g.ExpiresAt, g.RevokedAt,
                during.Count, during.Count > 0 ? during.Max() : null);
        }).ToList();
        return (views, synced);
    }

    public async Task<AccessChangeResult> ApproveAsync(Guid actorId, string actorName, Guid id, CancellationToken ct)
    {
        var grant = await db.SupportGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grant is null) return AccessChangeResult.NotFound;
        if (grant.Status != SupportGrantStatus.Pending) return AccessChangeResult.Invalid;

        var decided = await platform.DecideSupportAsync(tenant.Current.Slug, id, approve: true, actorName, ct);
        if (decided?.ExpiresAt is not { } expiresAt)
        {
            // Retirado pelo suporte ou já decidido: a próxima sincronização ajusta a lista.
            return AccessChangeResult.Invalid;
        }

        var now = clock.GetUtcNow();
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == grant.UserId, ct);
        if (member is null)
        {
            db.Members.Add(member = new Member
            {
                UserId = grant.UserId,
                Email = grant.RequesterEmail,
                DisplayName = grant.RequesterName,
                Status = MemberStatus.Active,
                CreatedAt = now,
            });
        }

        member.Status = MemberStatus.Active;

        // Papel anterior de suporte (expirado) sai antes do novo: um papel ativo por pessoa.
        await db.Roles.Where(r => r.UserId == grant.UserId && r.Role == Roles.PlatformSupport && r.RevokedAt == null)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.RevokedAt, now), ct);
        db.Roles.Add(new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = grant.UserId,
            Role = Roles.PlatformSupport,
            GrantedBy = actorId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
        });

        grant.Status = SupportGrantStatus.Approved;
        grant.DecidedBy = actorId;
        grant.DecidedAt = now;
        grant.ExpiresAt = expiresAt;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_approved", ResourceType: "support_grant", ResourceId: id.ToString(), UserId: actorId), ct);

        await NotifyDirectorsAsync(
            $"Acesso de suporte liberado — {tenant.Current.DisplayName}",
            $"{actorName} liberou o acesso de suporte de {grant.RequesterName} ({grant.RequesterEmail}) até " +
            $"{expiresAt.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy HH:mm} (horário de Brasília).\n\n" +
            $"Qualquer Diretor(a) pode revogar antes do prazo em:\n{SupportPageUrl()}", ct);
        return AccessChangeResult.Done;
    }

    public async Task<AccessChangeResult> DenyAsync(Guid actorId, string actorName, Guid id, CancellationToken ct)
    {
        var grant = await db.SupportGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grant is null) return AccessChangeResult.NotFound;
        if (grant.Status != SupportGrantStatus.Pending) return AccessChangeResult.Invalid;

        await platform.DecideSupportAsync(tenant.Current.Slug, id, approve: false, actorName, ct);
        grant.Status = SupportGrantStatus.Denied;
        grant.DecidedBy = actorId;
        grant.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_denied", ResourceType: "support_grant", ResourceId: id.ToString(), UserId: actorId), ct);
        return AccessChangeResult.Done;
    }

    public async Task<AccessChangeResult> RevokeAsync(Guid actorId, string actorName, Guid id, CancellationToken ct)
    {
        var grant = await db.SupportGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
        var now = clock.GetUtcNow();
        if (grant is null) return AccessChangeResult.NotFound;
        if (!grant.IsActive(now)) return AccessChangeResult.Invalid;

        await db.Roles.Where(r => r.UserId == grant.UserId && r.Role == Roles.PlatformSupport && r.RevokedAt == null)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedBy, actorId), ct);
        await db.Sessions.Where(s => s.UserId == grant.UserId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        grant.Status = SupportGrantStatus.Revoked;
        grant.RevokedBy = actorId;
        grant.RevokedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_revoked", ResourceType: "support_grant", ResourceId: id.ToString(), UserId: actorId), ct);

        // A escola já cortou o acesso; a plataforma é avisada para o login único também negar.
        await platform.RevokeSupportAsync(tenant.Current.Slug, id, actorName, ct);
        return AccessChangeResult.Done;
    }

    private string SupportPageUrl() => new Uri(urls.Value.TenantUrl(tenant.Current.Slug), "suporte").ToString();

    private async Task NotifyDirectorsAsync(string subject, string text, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var directors = await db.Members.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active
                && m.Roles.Any(r => r.Role == Roles.Director && r.RevokedAt == null && (r.ExpiresAt == null || r.ExpiresAt > now)))
            .Select(m => m.Email)
            .ToListAsync(ct);

        foreach (var to in directors)
        {
            await email.SendAsync(new EmailMessage(to, subject, text), ct);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pedidos de suporte: plataforma indisponível para a escola {Tenant} ({Error})")]
    private static partial void LogSyncFailed(ILogger logger, string tenant, string error);
}
