using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;

namespace Sinedu.SharedKernel.Tenancy;

/// <summary>Dados da escola resolvida para a requisição atual.</summary>
public interface ITenantContext
{
    string Slug { get; }
}

internal sealed class TenantContext(string slug) : ITenantContext
{
    public string Slug { get; } = slug;
}

/// <summary>
/// Resolve a escola pelo host. Qualquer host que não seja de escola recebe 404 genérico.
/// Fase 0: só valida o formato. A consulta ao registro de escolas, status e conexão
/// do banco entram no item F1-03.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, HostClassifier classifier)
{
    public const string ItemKey = "sinedu.tenant";

    public async Task InvokeAsync(HttpContext context, IDiagnosticContext diagnosticContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(diagnosticContext);

        var classification = classifier.Classify(context.Request.Host.Value);
        if (classification.Kind != HostKind.Tenant || classification.TenantSlug is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Items[ItemKey] = new TenantContext(classification.TenantSlug);
        diagnosticContext.Set("Tenant", classification.TenantSlug);

        // Todo log desta requisição carrega a escola (nunca dados de alunos).
        using (LogContext.PushProperty("Tenant", classification.TenantSlug))
        {
            await next(context);
        }
    }
}

public static class TenantHttpContextExtensions
{
    public static ITenantContext GetTenant(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(TenantResolutionMiddleware.ItemKey, out var value) && value is ITenantContext tenant
            ? tenant
            : throw new InvalidOperationException("Requisição sem escola resolvida.");
    }
}
