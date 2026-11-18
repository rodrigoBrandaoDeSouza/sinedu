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
        services.AddHttpClient<IPlatformMemberships, PlatformMembershipsClient>()
            .AddHttpMessageHandler(sp => new AccountBackchannelHandler(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SineduUrls>>()));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
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

    private static Guid ActorId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

    private static string[] ActorRoles(ClaimsPrincipal user) => user.FindAll("role").Select(c => c.Value).ToArray();
}

public sealed record InviteMemberRequest(string Email, string FullName, string Role);
