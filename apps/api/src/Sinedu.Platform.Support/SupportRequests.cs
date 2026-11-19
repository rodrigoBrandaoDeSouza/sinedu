using Microsoft.EntityFrameworkCore;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Platform.Support;

public enum SupportChange
{
    Done,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>Pedido como a escola o vê (API interna). Sem dados de aluno: quem pede, por quê, por quanto tempo.</summary>
public sealed record TenantSupportRequest(
    Guid Id, Guid UserId, string RequesterName, string RequesterEmail, string Reason, int DurationHours,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

/// <summary>Regras do acesso de suporte do lado da plataforma (P-11). A decisão é sempre da escola.</summary>
public sealed class SupportRequests(SupportDbContext db, TenantsDbContext tenants, IAuditLog audit, TimeProvider clock) : ISupportAccessDirectory
{
    public async Task<(SupportChange Result, SupportAccessRequest? Request)> CreateAsync(
        Guid requesterId, string requesterName, string requesterEmail, Guid tenantId, string reason, int hours, CancellationToken ct)
    {
        reason = reason?.Trim() ?? "";
        if (hours is < 1 or > SupportAccessRequest.MaxHours || reason.Length is < 10 or > SupportAccessRequest.ReasonMaxLength)
        {
            return (SupportChange.Invalid, null);
        }

        var tenant = await tenants.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is not { Status: TenantStatus.Active })
        {
            return (SupportChange.NotFound, null);
        }

        var now = clock.GetUtcNow();
        var open = await db.Requests.AnyAsync(r => r.TenantId == tenantId && r.RequestedBy == requesterId
            && (r.Status == SupportRequestStatus.Pending || (r.Status == SupportRequestStatus.Approved && r.ExpiresAt > now)), ct);
        if (open)
        {
            return (SupportChange.Conflict, null);
        }

        var request = new SupportAccessRequest
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            TenantSlug = tenant.Slug,
            TenantName = tenant.DisplayName,
            RequestedBy = requesterId,
            RequesterName = requesterName,
            RequesterEmail = requesterEmail,
            Reason = reason,
            DurationHours = hours,
            Status = SupportRequestStatus.Pending,
            CreatedAt = now,
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_requested", ResourceType: "tenant", ResourceId: tenant.Id.ToString(), UserId: requesterId), ct);
        return (SupportChange.Done, request);
    }

    /// <summary>Painel: SUPER_ADMIN vê todos; SUPORTE só os próprios.</summary>
    public async Task<List<SupportAccessRequest>> ListAsync(Guid userId, bool all, CancellationToken ct) =>
        await db.Requests.AsNoTracking()
            .Where(r => all || r.RequestedBy == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

    public async Task<SupportChange> CancelAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id && r.RequestedBy == userId, ct);
        if (request is null) return SupportChange.NotFound;
        if (request.Status != SupportRequestStatus.Pending) return SupportChange.Conflict;

        request.Status = SupportRequestStatus.Cancelled;
        request.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_cancelled", ResourceType: "support_request", ResourceId: id.ToString(), UserId: userId), ct);
        return SupportChange.Done;
    }

    /// <summary>Escola: pedidos em aberto e liberações dos últimos 30 dias dela.</summary>
    public async Task<List<TenantSupportRequest>> ForTenantAsync(string tenantSlug, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now.AddDays(-30);
        var rows = await db.Requests.AsNoTracking()
            .Where(r => r.TenantSlug == tenantSlug && (r.Status == SupportRequestStatus.Pending || r.CreatedAt >= since))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(r => new TenantSupportRequest(
            r.Id, r.RequestedBy, r.RequesterName, r.RequesterEmail, r.Reason, r.DurationHours, r.DisplayStatus(now), r.CreatedAt, r.ExpiresAt)).ToList();
    }

    /// <summary>Decisão do Diretor, registrada pela escola. Devolve o prazo quando aprovado.</summary>
    public async Task<(SupportChange Result, TenantSupportRequest? Request)> DecideAsync(
        string tenantSlug, Guid id, bool approve, string decidedByName, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id && r.TenantSlug == tenantSlug, ct);
        if (request is null) return (SupportChange.NotFound, null);
        if (request.Status != SupportRequestStatus.Pending) return (SupportChange.Conflict, null);

        var now = clock.GetUtcNow();
        request.Status = approve ? SupportRequestStatus.Approved : SupportRequestStatus.Denied;
        request.DecidedAt = now;
        request.DecidedByName = decidedByName.Trim();
        request.ExpiresAt = approve ? now.AddHours(request.DurationHours) : null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry(approve ? "support_approved" : "support_denied", ResourceType: "support_request", ResourceId: id.ToString()), ct);
        return (SupportChange.Done, new TenantSupportRequest(
            request.Id, request.RequestedBy, request.RequesterName, request.RequesterEmail, request.Reason, request.DurationHours,
            request.DisplayStatus(now), request.CreatedAt, request.ExpiresAt));
    }

    public async Task<SupportChange> RevokeAsync(string tenantSlug, Guid id, string revokedByName, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id && r.TenantSlug == tenantSlug, ct);
        if (request is null) return SupportChange.NotFound;
        if (request.Status != SupportRequestStatus.Approved) return SupportChange.Conflict;

        request.Status = SupportRequestStatus.Revoked;
        request.EndedAt = clock.GetUtcNow();
        request.DecidedByName = revokedByName.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("support_revoked", ResourceType: "support_request", ResourceId: id.ToString()), ct);
        return SupportChange.Done;
    }

    public async Task<SupportGrantInfo?> ActiveGrantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var grant = await db.Requests.AsNoTracking()
            .Where(r => r.RequestedBy == userId && r.TenantId == tenantId && r.Status == SupportRequestStatus.Approved && r.ExpiresAt > now)
            .OrderByDescending(r => r.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
        return grant is null ? null : new SupportGrantInfo(grant.TenantId, grant.TenantSlug, grant.TenantName, grant.ExpiresAt!.Value);
    }

    public async Task<IReadOnlyList<SupportGrantInfo>> ActiveGrantsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var grants = await db.Requests.AsNoTracking()
            .Where(r => r.RequestedBy == userId && r.Status == SupportRequestStatus.Approved && r.ExpiresAt > now)
            .OrderBy(r => r.TenantName)
            .ToListAsync(cancellationToken);
        return grants.Select(g => new SupportGrantInfo(g.TenantId, g.TenantSlug, g.TenantName, g.ExpiresAt!.Value)).ToList();
    }
}
