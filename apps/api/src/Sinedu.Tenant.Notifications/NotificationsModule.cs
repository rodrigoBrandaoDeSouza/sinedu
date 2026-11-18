using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Notifications;

public sealed record AuthorizeRequest(Guid TemplateId, IReadOnlyList<Guid> GuardianIds, Guid RequestId, bool ConfirmDuplicate = false);

public sealed record TemplateRequest(string? Name, string? Subject, string? Body, bool IsActive = true);

/// <summary>
/// Comunicação com responsáveis (spec §6): prévia, autorização explícita, envio por e-mail com fila e reenvio,
/// idempotência. Só Direção e Pedagogia. Mensagens nunca carregam dados de Saúde ou Proteção nem o conteúdo do fato.
/// </summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<NotificationsDbContext>(NotificationsDbContext.Schema);
        services.AddScoped<INotificationProvider, EmailNotificationProvider>();
        services.AddScoped<INotificationProvider, WhatsAppNotificationProvider>();
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationRetries, NotificationRetries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var occurrence = endpoints.MapGroup("/api/occurrences/{occurrenceId:guid}/communications")
            .RequireAuthorization(Policies.PedagogicalStaff).AddEndpointFilter(TranslateErrors);

        occurrence.MapGet("/preview", async (Guid occurrenceId, Guid? templateId, NotificationService s, CancellationToken ct) =>
            await s.PreviewAsync(occurrenceId, templateId, ct) is { } p ? Results.Ok(p) : Results.NotFound());

        occurrence.MapPost("/", async (Guid occurrenceId, AuthorizeRequest body, ClaimsPrincipal user, NotificationService s, CancellationToken ct) =>
            await s.AuthorizeAsync(occurrenceId, body.TemplateId, body.GuardianIds, body.RequestId, body.ConfirmDuplicate, ActorOf(user), ct) is { } list
                ? Results.Ok(list)
                : Results.NotFound());

        occurrence.MapGet("/", async (Guid occurrenceId, NotificationService s, CancellationToken ct) =>
            Results.Ok((await s.ListAsync(occurrenceId, null, null, 1, ct)).Items));

        var api = endpoints.MapGroup("/api/communications").RequireAuthorization(Policies.PedagogicalStaff).AddEndpointFilter(TranslateErrors);

        api.MapGet("/", async (Guid? studentId, NotificationStatus? status, int? page, NotificationService s, CancellationToken ct) =>
        {
            var (items, total) = await s.ListAsync(null, studentId, status, page ?? 1, ct);
            return Results.Ok(new { total, page = page ?? 1, pageSize = NotificationService.PageSize, items });
        });

        api.MapGet("/channels", (NotificationService s) => Results.Ok(s.Channels()));

        api.MapGet("/templates", async (NotificationService s, CancellationToken ct) => Results.Ok(await s.TemplatesAsync(activeOnly: false, ct)));

        api.MapPut("/templates/{id:guid}", async (Guid id, TemplateRequest body, NotificationService s, CancellationToken ct) =>
            await s.UpdateTemplateAsync(id, body.Name, body.Subject, body.Body, body.IsActive, ct) is { } t ? Results.Ok(t) : Results.NotFound())
            .RequireAuthorization(Policies.SchoolManager);

        api.MapPost("/{id:guid}/retry", async (Guid id, NotificationService s, CancellationToken ct) =>
            await s.RetryAsync(id, ct) is { } n ? Results.Ok(n) : Results.NotFound());

        api.MapPost("/{id:guid}/cancel", async (Guid id, NotificationService s, CancellationToken ct) =>
            await s.CancelAsync(id, ct) is { } n ? Results.Ok(n) : Results.NotFound());
    }

    private static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (CommunicationValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(ex.Errors));
        }
        catch (CommunicationConflictException ex)
        {
            return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static Occurrences.Actor ActorOf(ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue("sub")!), user.FindFirstValue("name") ?? user.FindFirstValue("email") ?? "Equipe");
}

/// <summary>Entrada da fila de reenvio, chamada pelo worker do host para cada escola ativa.</summary>
public interface INotificationRetries
{
    Task<int> ProcessDueAsync(CancellationToken ct);
}

internal sealed class NotificationRetries(NotificationService service) : INotificationRetries
{
    public Task<int> ProcessDueAsync(CancellationToken ct) => service.ProcessDueAsync(ct);
}
