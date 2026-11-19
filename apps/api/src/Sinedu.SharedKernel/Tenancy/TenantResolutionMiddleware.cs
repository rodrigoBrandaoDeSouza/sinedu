using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.SharedKernel.Tenancy;

/// <summary>Escola da requisição atual, com tudo que é necessário para acessar os recursos dela.</summary>
public interface ITenantContext
{
    Guid Id { get; }

    string Slug { get; }

    string DisplayName { get; }

    /// <summary>Conexão com o banco DESTA escola (papel da aplicação, sem privilégios de DDL).</summary>
    string ConnectionString { get; }

    string TransitKeyName { get; }

    string BucketName { get; }

    /// <summary>Escola em encerramento (P-13): só login e a exportação de encerramento pela Direção.</summary>
    bool Closing { get; }
}

public sealed record TenantContext(
    Guid Id, string Slug, string DisplayName, string ConnectionString, string TransitKeyName, string BucketName, bool Closing = false) : ITenantContext;

/// <summary>Guarda a escola corrente no escopo (requisição ou job). Não existe caminho para trocar de escola no meio do escopo.</summary>
public sealed class TenantScope
{
    private ITenantContext? _current;

    public ITenantContext Current => _current ?? throw new InvalidOperationException("Nenhuma escola resolvida neste escopo.");

    public bool HasTenant => _current is not null;

    public void Set(ITenantContext tenant)
    {
        if (_current is not null && _current.Id != tenant.Id)
        {
            throw new InvalidOperationException("O escopo já pertence a outra escola.");
        }

        _current = tenant;
    }
}

public enum TenantResolutionStatus
{
    NotFound,
    Unavailable,
    Maintenance,
    Ready,
}

public sealed record TenantResolution(TenantResolutionStatus Status, ITenantContext? Tenant = null);

/// <summary>Resolve uma escola pelo slug: registro + credenciais. Implementado pelo host da escola.</summary>
public interface ITenantResolver
{
    Task<TenantResolution> ResolveAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolve a escola pelo host. Qualquer host que não seja de escola, ou escola inexistente, recebe 404.
/// Escola suspensa/encerrada recebe 503 sem login (P12); schema divergente recebe 503 de manutenção.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, HostClassifier classifier)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, TenantScope scope, IDiagnosticContext diagnosticContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(diagnosticContext);

        var classification = classifier.Classify(context.Request.Host.Value);
        if (classification.Kind != HostKind.Tenant || classification.TenantSlug is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var resolution = await resolver.ResolveAsync(classification.TenantSlug, context.RequestAborted);
        switch (resolution.Status)
        {
            case TenantResolutionStatus.NotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            case TenantResolutionStatus.Unavailable:
                await WriteUnavailableAsync(context, "tenant_unavailable", "Esta escola está temporariamente indisponível.");
                return;
            case TenantResolutionStatus.Maintenance:
                await WriteUnavailableAsync(context, "maintenance", "A escola está em manutenção. Tente novamente em alguns minutos.");
                return;
        }

        var tenant = resolution.Tenant!;
        scope.Set(tenant);
        diagnosticContext.Set("Tenant", tenant.Slug);

        // Todo log desta requisição carrega a escola (nunca dados de alunos).
        using (LogContext.PushProperty("Tenant", tenant.Slug))
        {
            await next(context);
        }
    }

    private static Task WriteUnavailableAsync(HttpContext context, string code, string title)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Results.Problem(title: title, statusCode: 503, extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
    }
}

public static class TenantServiceExtensions
{
    /// <summary>DbContext de módulo da escola: sempre aberto com a conexão da escola do escopo atual.</summary>
    public static IServiceCollection AddTenantDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((sp, options) =>
            options.UseSineduPostgres(sp.GetRequiredService<TenantScope>().Current.ConnectionString, schema));
}

public static class TenantHttpContextExtensions
{
    public static ITenantContext GetTenant(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RequestServices.GetRequiredService<TenantScope>().Current;
    }
}
