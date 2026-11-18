using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Sinedu.SharedKernel.Audit;

/// <summary>Grava eventos no banco do contexto atual (escola ou plataforma).</summary>
public sealed partial class AuditLog(AuditDbContext db, IHttpContextAccessor http, TimeProvider clock, ILogger<AuditLog> logger) : IAuditLog
{
    public const string SupportSessionClaim = "sinedu:support";

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var context = http.HttpContext;
        var user = context?.User;

        db.Events.Add(new AuditEvent
        {
            OccurredAt = clock.GetUtcNow(),
            UserId = entry.UserId ?? ParseUserId(user),
            StudentId = entry.StudentId,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            Result = entry.Result,
            IsSupportSession = user?.HasClaim(SupportSessionClaim, "true") == true,
            Ip = context?.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = context?.TraceIdentifier,
        });

        await db.SaveChangesAsync(cancellationToken);
        LogAudit(logger, entry.Action, entry.Result);
    }

    private static Guid? ParseUserId(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue("sub"), out var id) ? id : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Auditoria: {Action} ({Result})")]
    private static partial void LogAudit(ILogger logger, string action, AuditResult result);
}
