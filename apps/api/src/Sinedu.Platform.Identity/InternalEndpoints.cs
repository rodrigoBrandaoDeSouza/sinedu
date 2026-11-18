using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Identity;

/// <summary>
/// API serviço→serviço usada pelas escolas (token client_credentials com escopo de vínculos).
/// A escola nunca escreve no banco da plataforma diretamente.
/// </summary>
internal static class InternalEndpoints
{
    public const string Policy = "internal:vinculos";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapAccountApi("/internal").RequireAuthorization(Policy);

        group.MapPost("/invitations", async (InviteRequest body, ITenantDirectory tenants, AccountService accounts, CancellationToken ct) =>
        {
            var tenant = await tenants.FindBySlugAsync(body.TenantSlug, ct);
            if (tenant is not { Status: TenantStatus.Active }) return Results.NotFound();
            if (!new EmailAddressAttribute().IsValid(body.Email) || string.IsNullOrWhiteSpace(body.FullName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Informe nome e e-mail válidos."] });
            }

            try
            {
                var userId = await accounts.InviteToTenantAsync(tenant.Id, tenant.Slug, tenant.DisplayName, body.Email, body.FullName, body.Privileged, ct);
                return Results.Ok(new { userId });
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(
                    title: "Esta conta é da equipe da plataforma e não pode receber papéis em escolas.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = "platform_account" });
            }
        });

        group.MapPost("/memberships/sync", async (SyncRequest body, ITenantDirectory tenants, AccountService accounts, CancellationToken ct) =>
        {
            var tenant = await tenants.FindBySlugAsync(body.TenantSlug, ct);
            if (tenant is null) return Results.NotFound();

            await accounts.SyncMembershipAsync(tenant.Id, body.UserId, body.Active, body.Privileged, ct);
            return Results.NoContent();
        });
    }
}

internal sealed record InviteRequest(string TenantSlug, string Email, string FullName, bool Privileged);

internal sealed record SyncRequest(string TenantSlug, Guid UserId, bool Active, bool Privileged);
