using Microsoft.AspNetCore.Routing;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.Host.Auth;

/// <summary>
/// Sessão de suporte da plataforma (P-11, D17): somente leitura e tudo auditado como <c>support_session</c>.
/// O que o suporte enxerga continua decidido pelas políticas de cada endpoint (nunca ocorrências, indicadores,
/// Saúde ou Proteção); aqui fica a regra transversal: qualquer método que muda estado → 403.
/// </summary>
public static class SupportSession
{
    public static bool IsSupportOnly(IReadOnlyCollection<string> roles) =>
        roles.Count > 0 && roles.All(r => r == Roles.PlatformSupport);
}

internal sealed class SupportSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAuditLog audit)
    {
        if (context.User.HasClaim(AuditLog.SupportSessionClaim, "true") != true)
        {
            await next(context);
            return;
        }

        var method = context.Request.Method;
        var readOnly = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        var logout = HttpMethods.IsPost(method) && context.Request.Path.StartsWithSegments("/auth/logout");
        var resource = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
        var studentId = Guid.TryParse(context.GetRouteValue("studentId") as string, out var sid) ? sid : (Guid?)null;
        if (studentId is null && resource?.StartsWith("/api/students/{id", StringComparison.Ordinal) == true
            && Guid.TryParse(context.GetRouteValue("id") as string, out var id))
        {
            studentId = id;
        }

        if (!readOnly && !logout)
        {
            await audit.WriteAsync(new AuditEntry(SupportAccessService.SupportAction, AuditResult.Denied, method, resource, studentId), context.RequestAborted);
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "O acesso de suporte é somente leitura.").ExecuteAsync(context);
            return;
        }

        await next(context);

        // Só a rota (modelo, sem valores) e o aluno, quando houver — nunca conteúdo.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var result = context.Response.StatusCode < 400 ? AuditResult.Allowed : AuditResult.Denied;
            await audit.WriteAsync(new AuditEntry(SupportAccessService.SupportAction, result, method, resource, studentId), CancellationToken.None);
        }
    }
}
