using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Access;

public sealed class AccessModule : IModule
{
    public string Name => "Access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantDbContext<AccessDbContext>(AccessDbContext.Schema);
        services.AddScoped<AccessService>();
        services.AddScoped<SupportAccessService>();
        services.AddScoped<PlatformAccountEventsSync>();
        services.AddHttpClient<IPlatformMemberships, PlatformMembershipsClient>()
            .AddHttpMessageHandler(sp => new AccountBackchannelHandler(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SineduUrls>>()));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapSupport(endpoints);

        var group = endpoints.MapGroup("/api/access").RequireAuthorization(Policies.SchoolConfig);

        group.MapGet("/members", async (AccessDbContext db, CancellationToken ct) =>
        {
            var members = await db.Members.AsNoTracking()
                .Where(m => m.Status == MemberStatus.Active)
                .Select(m => new
                {
                    m.UserId,
                    m.DisplayName,
                    m.Email,
                    m.LastSeenAt,
                    Roles = m.Roles.Where(r => r.RevokedAt == null).Select(r => new { r.Role, r.GrantedAt, r.ExpiresAt }),
                })
                .OrderBy(m => m.DisplayName)
                .ToListAsync(ct);
            return Results.Ok(members);
        });

        group.MapPost("/invitations", async (InviteMemberRequest body, ClaimsPrincipal user, AccessService access, CancellationToken ct) =>
        {
            if (!new EmailAddressAttribute().IsValid(body.Email) || string.IsNullOrWhiteSpace(body.FullName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Informe nome e e-mail válidos."] });
            }

            var (result, userId) = await access.InviteAsync(ActorId(user), ActorRoles(user), body.Email, body.FullName, body.Role, ct);
            return result switch
            {
                AccessChangeResult.Done => Results.Ok(new { userId }),
                AccessChangeResult.Forbidden => Results.Problem(statusCode: 403, title: "Seu papel não pode conceder este acesso."),
                AccessChangeResult.PlatformAccount => Results.Problem(statusCode: 409, title: "Esta conta é da equipe da plataforma e não pode receber papéis na escola."),
                _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Papel inválido."] }),
            };
        });

        group.MapDelete("/members/{userId:guid}/roles/{role}", async (Guid userId, string role, ClaimsPrincipal user, AccessService access, CancellationToken ct) =>
        {
            var result = await access.RevokeAsync(ActorId(user), ActorRoles(user), userId, role, ct);
            return result switch
            {
                AccessChangeResult.Done => Results.NoContent(),
                AccessChangeResult.Forbidden => Results.Problem(statusCode: 403, title: "Seu papel não pode revogar este acesso."),
                AccessChangeResult.LastDirector => Results.Problem(statusCode: 409, title: "A escola precisa de pelo menos um Diretor ativo."),
                _ => Results.NotFound(),
            };
        });
    }

    /// <summary>Acesso de suporte (P-11): Diretor e Admin técnico veem o registro; só o Diretor decide e revoga.</summary>
    private static void MapSupport(IEndpointRouteBuilder endpoints)
    {
        var support = endpoints.MapGroup("/api/access/support");

        support.MapGet("/", async (SupportAccessService service, CancellationToken ct) =>
        {
            var (grants, synced) = await service.ListAsync(ct);
            return Results.Ok(new { grants, synced });
        }).RequireAuthorization(Policies.SchoolConfig);

        support.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, SupportAccessService service, CancellationToken ct) =>
            Decision(await service.ApproveAsync(ActorId(user), ActorName(user), id, ct)))
            .RequireAuthorization(Policies.SchoolManager);

        support.MapPost("/{id:guid}/deny", async (Guid id, ClaimsPrincipal user, SupportAccessService service, CancellationToken ct) =>
            Decision(await service.DenyAsync(ActorId(user), ActorName(user), id, ct)))
            .RequireAuthorization(Policies.SchoolManager);

        support.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, SupportAccessService service, CancellationToken ct) =>
            Decision(await service.RevokeAsync(ActorId(user), ActorName(user), id, ct)))
            .RequireAuthorization(Policies.SchoolManager);
    }

    private static IResult Decision(AccessChangeResult result) => result switch
    {
        AccessChangeResult.Done => Results.NoContent(),
        AccessChangeResult.Invalid => Results.Problem(statusCode: 409, title: "Este pedido já foi decidido, retirado ou expirou. Atualize a página."),
        _ => Results.NotFound(),
    };

    private static string ActorName(ClaimsPrincipal user) => user.FindFirstValue("name") ?? "Direção da escola";

    private static Guid ActorId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

    private static string[] ActorRoles(ClaimsPrincipal user) => user.FindAll("role").Select(c => c.Value).ToArray();
}

public sealed record InviteMemberRequest(string Email, string FullName, string Role);
