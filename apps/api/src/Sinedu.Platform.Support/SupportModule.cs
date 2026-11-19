using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Support;

/// <summary>
/// Acesso de suporte (P-11, D17): o suporte pede no painel; o Diretor decide na escola; o papel temporário
/// SUPORTE_PLATAFORMA (somente leitura de dados comuns, até 24 h) vive no banco da escola.
/// </summary>
public sealed class SupportModule : IModule
{
    public string Name => "Support";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Platform") ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");
        services.AddDbContext<SupportDbContext>(o => o.UseSineduPostgres(connectionString, SupportDbContext.Schema));
        services.AddScoped<SupportRequests>();
        services.AddScoped<ISupportAccessDirectory>(sp => sp.GetRequiredService<SupportRequests>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapPanel(endpoints);
        MapInternal(endpoints);
    }

    private static void MapPanel(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapPanelApi("/support").RequireAuthorization(Policies.PlatformStaff);

        api.MapGet("/requests", async (ClaimsPrincipal user, SupportRequests support, TimeProvider clock, IOptions<SineduUrls> urls, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var all = user.HasClaim("role", Roles.SuperAdmin);
            var rows = await support.ListAsync(UserId(user), all, ct);
            return Results.Ok(rows.Select(r => new
            {
                r.Id,
                tenant = new { id = r.TenantId, slug = r.TenantSlug, name = r.TenantName },
                requester = r.RequesterName,
                mine = r.RequestedBy == UserId(user),
                r.Reason,
                r.DurationHours,
                status = r.DisplayStatus(now),
                r.CreatedAt,
                r.DecidedAt,
                r.DecidedByName,
                r.ExpiresAt,
                url = r.IsActive(now) && r.RequestedBy == UserId(user) ? urls.Value.TenantUrl(r.TenantSlug).ToString() : null,
            }));
        });

        api.MapPost("/requests", async (CreateSupportRequest body, ClaimsPrincipal user, SupportRequests support, CancellationToken ct) =>
        {
            var (result, request) = await support.CreateAsync(
                UserId(user), user.FindFirstValue("name") ?? "Suporte", user.FindFirstValue("email") ?? "", body.TenantId, body.Reason, body.DurationHours, ct);
            return result switch
            {
                SupportChange.Done => Results.Ok(new { request!.Id }),
                SupportChange.NotFound => Results.NotFound(),
                SupportChange.Conflict => Results.Problem(statusCode: 409, title: "Você já tem um pedido em aberto ou um acesso vigente nesta escola."),
                _ => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = [$"Explique o motivo (10 a {SupportAccessRequest.ReasonMaxLength} caracteres, sem dados de aluno) e escolha de 1 a {SupportAccessRequest.MaxHours} horas."],
                }),
            };
        });

        api.MapPost("/requests/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, SupportRequests support, CancellationToken ct) =>
            await support.CancelAsync(UserId(user), id, ct) switch
            {
                SupportChange.Done => Results.NoContent(),
                SupportChange.Conflict => Results.Problem(statusCode: 409, title: "Só é possível retirar um pedido que ainda aguarda a escola."),
                _ => Results.NotFound(),
            });
    }

    /// <summary>Usado pelas escolas (token client_credentials). Cada escola só enxerga e decide os próprios pedidos.</summary>
    private static void MapInternal(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapAccountApi("/internal/support-requests").RequireAuthorization(Policies.PlatformInternal);

        api.MapGet("/", async (string tenantSlug, SupportRequests support, CancellationToken ct) =>
            Results.Ok(await support.ForTenantAsync(tenantSlug, ct)));

        api.MapPost("/{id:guid}/decision", async (Guid id, SupportDecisionRequest body, SupportRequests support, CancellationToken ct) =>
        {
            var (result, request) = await support.DecideAsync(body.TenantSlug, id, body.Approve, body.DecidedBy, ct);
            return result switch
            {
                SupportChange.Done => Results.Ok(request),
                SupportChange.Conflict => Results.Conflict(),
                _ => Results.NotFound(),
            };
        });

        api.MapPost("/{id:guid}/revoke", async (Guid id, SupportRevokeRequest body, SupportRequests support, CancellationToken ct) =>
            await support.RevokeAsync(body.TenantSlug, id, body.RevokedBy, ct) switch
            {
                SupportChange.Done => Results.NoContent(),
                SupportChange.Conflict => Results.Conflict(),
                _ => Results.NotFound(),
            });
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
}

public sealed record CreateSupportRequest(Guid TenantId, string Reason, int DurationHours);

public sealed record SupportDecisionRequest(string TenantSlug, bool Approve, string DecidedBy);

public sealed record SupportRevokeRequest(string TenantSlug, string RevokedBy);
