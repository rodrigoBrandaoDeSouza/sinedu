using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Tenants;

public sealed class TenantsModule : IModule
{
    public string Name => "Tenants";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");
        services.AddDbContext<TenantsDbContext>(o => o.UseSineduPostgres(connectionString, TenantsDbContext.Schema));
        services.AddOptions<TenantDatabaseOptions>().Bind(configuration.GetSection(TenantDatabaseOptions.Section));
        services.AddScoped<ITenantDirectory, PlatformTenantDirectory>();
        services.AddScoped<TenantProvisioner>();
        services.AddSingleton<ProvisioningScheduler>();
        services.AddHostedService<ProvisioningWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tenants = endpoints.MapPanelApi("/tenants").RequireAuthorization(Policies.PlatformStaff);
        TenantSmtpEndpoints.Map(tenants);

        tenants.MapGet("/", async (TenantsDbContext db, Microsoft.Extensions.Options.IOptions<SineduUrls> urls, CancellationToken ct) =>
        {
            var rows = await db.Tenants.AsNoTracking()
                .OrderBy(t => t.DisplayName)
                .Select(t => new { t.Id, t.Slug, t.DisplayName, t.NetworkId, Network = t.Network != null ? t.Network.Name : null, t.Status, t.StatusReason, t.SchemaVersion, t.CreatedAt })
                .ToListAsync(ct);
            return rows.Select(t => new TenantSummary(
                t.Id, t.Slug, t.DisplayName, urls.Value.TenantUrl(t.Slug), t.Network, t.Status, t.StatusReason, t.SchemaVersion, t.CreatedAt, t.NetworkId));
        });

        tenants.MapGet("/{id:guid}", async (Guid id, TenantsDbContext db, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().Include(t => t.ProvisioningSteps).Include(t => t.Network)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            return tenant is null
                ? Results.NotFound()
                : Results.Ok(new TenantDetails(
                    tenant.Id, tenant.Slug, tenant.DisplayName, tenant.Network?.Name, tenant.InstitutionalCode, tenant.Status,
                    tenant.StatusReason, tenant.SchemaVersion, tenant.CreatedAt,
                    tenant.ProvisioningSteps.OrderBy(s => s.StartedAt)
                        .Select(s => new StepView(s.Step.ToString(), s.Status.ToString(), s.StartedAt, s.FinishedAt, s.Error)).ToList()));
        });

        tenants.MapPost("/", CreateTenantAsync).RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapPost("/{id:guid}/retry", async (Guid id, TenantsDbContext db, ProvisioningScheduler queue, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tenant is null) return Results.NotFound();
            if (tenant.Status != TenantStatus.ProvisioningFailed) return Results.Conflict();

            await queue.EnqueueAsync(id);
            return Results.Accepted();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapPost("/{id:guid}/suspend", (Guid id, ReasonRequest body, TenantsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
            ChangeStatusAsync(id, TenantStatus.Active, TenantStatus.Suspended, body.Reason, "tenant_suspended", db, audit, clock, ct))
            .RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapPost("/{id:guid}/reactivate", (Guid id, ReasonRequest body, TenantsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
            ChangeStatusAsync(id, TenantStatus.Suspended, TenantStatus.Active, body.Reason, "tenant_reactivated", db, audit, clock, ct))
            .RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapPost("/migrate", async (TenantProvisioner provisioner, CancellationToken ct) =>
        {
            var (updated, error) = await provisioner.MigrateOutdatedAsync(ct);
            return Results.Ok(new { updated, error, version = TenantSchema.Version });
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        var networks = endpoints.MapPanelApi("/networks").RequireAuthorization(Policies.PlatformStaff);
        // P-04 (D5): rede só agrupa escolas no painel. Sem visão de rede, sem dado de aluno.
        networks.MapGet("/", async (TenantsDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Tenants.AsNoTracking().Where(t => t.NetworkId != null && t.Status != TenantStatus.Closed)
                .GroupBy(t => t.NetworkId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count, ct);
            var list = await db.Networks.AsNoTracking().OrderBy(n => n.Name).ToListAsync(ct);
            return list.Select(n => new { n.Id, n.Name, n.Kind, n.Uf, n.Municipality, n.CreatedAt, schools = counts.GetValueOrDefault(n.Id) });
        });

        networks.MapPost("/", async (CreateNetworkRequest body, TenantsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = await ValidateNetworkAsync(body, null, db, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var network = new Network { Id = Guid.CreateVersion7(), Name = body.Name.Trim(), CreatedAt = clock.GetUtcNow() };
            Apply(network, body);
            db.Networks.Add(network);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("network_created", ResourceType: "network", ResourceId: network.Id.ToString()), ct);
            return Results.Created($"/api/networks/{network.Id}", network);
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        networks.MapPut("/{id:guid}", async (Guid id, CreateNetworkRequest body, TenantsDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var network = await db.Networks.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (network is null) return Results.NotFound();
            var errors = await ValidateNetworkAsync(body, id, db, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            Apply(network, body);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("network_updated", ResourceType: "network", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        networks.MapDelete("/{id:guid}", async (Guid id, TenantsDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            if (await db.Tenants.AnyAsync(t => t.NetworkId == id, ct))
            {
                return Results.Problem(statusCode: 409, title: "Desvincule as escolas antes de excluir a rede.");
            }

            var removed = await db.Networks.Where(n => n.Id == id).ExecuteDeleteAsync(ct);
            if (removed == 0) return Results.NotFound();
            await audit.WriteAsync(new AuditEntry("network_deleted", ResourceType: "network", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        // Vincula (ou desvincula, com null) uma escola existente a uma rede.
        tenants.MapPut("/{id:guid}/network", async (Guid id, TenantNetworkRequest body, TenantsDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tenant is null) return Results.NotFound();
            if (body.NetworkId is { } nid && !await db.Networks.AnyAsync(n => n.Id == nid, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["networkId"] = ["Rede inválida."] });
            }

            tenant.NetworkId = body.NetworkId;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("tenant_network_changed", ResourceType: "tenant", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);
    }

    private static async Task<Dictionary<string, string[]>> ValidateNetworkAsync(CreateNetworkRequest body, Guid? id, TenantsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = body.Name?.Trim() ?? "";
        if (name.Length is < 3 or > 200) errors["name"] = ["Use de 3 a 200 caracteres."];
        else if (await db.Networks.AnyAsync(n => n.Id != id && n.Name == name, ct)) errors["name"] = ["Já existe uma rede com este nome."];
        if (!Enum.IsDefined(body.Kind)) errors["kind"] = ["Escolha o tipo."];
        var uf = body.Uf?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(uf) && (uf.Length != 2 || !uf.All(char.IsAsciiLetterUpper))) errors["uf"] = ["Use a sigla do estado (ex.: PR)."];
        if (body.Municipality?.Trim().Length > 120) errors["municipality"] = ["Use no máximo 120 caracteres."];
        return errors;
    }

    private static void Apply(Network network, CreateNetworkRequest body)
    {
        network.Name = body.Name.Trim();
        network.Kind = body.Kind;
        network.Uf = string.IsNullOrWhiteSpace(body.Uf) ? null : body.Uf.Trim().ToUpperInvariant();
        network.Municipality = string.IsNullOrWhiteSpace(body.Municipality) ? null : body.Municipality.Trim();
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest body, ClaimsPrincipal user, TenantsDbContext db, ProvisioningScheduler queue, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var slug = body.Slug.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>();
        if (!HostClassifier.IsValidSlug(slug) || HostClassifier.ReservedSubdomains.Contains(slug))
        {
            errors["slug"] = ["Use de 3 a 63 letras minúsculas, números ou hífen. Alguns nomes são reservados."];
        }
        else if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
        {
            errors["slug"] = ["Este subdomínio já está em uso."];
        }

        if (string.IsNullOrWhiteSpace(body.DisplayName)) errors["displayName"] = ["Informe o nome da escola."];
        if (!new EmailAddressAttribute().IsValid(body.FirstDirectorEmail)) errors["firstDirectorEmail"] = ["E-mail inválido."];
        if (string.IsNullOrWhiteSpace(body.FirstDirectorName)) errors["firstDirectorName"] = ["Informe o nome do diretor."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var tenant = Tenant.Create(Guid.CreateVersion7(), slug, body.DisplayName.Trim(), body.FirstDirectorEmail.Trim().ToLowerInvariant(),
            body.FirstDirectorName.Trim(), clock.GetUtcNow());
        tenant.NetworkId = body.NetworkId;
        tenant.InstitutionalCode = body.InstitutionalCode;
        tenant.CreatedBy = Guid.TryParse(user.FindFirstValue("sub"), out var userId) ? userId : null;
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditEntry("tenant_created", ResourceType: "tenant", ResourceId: tenant.Id.ToString()), ct);
        await queue.EnqueueAsync(tenant.Id);
        return Results.Accepted($"/api/tenants/{tenant.Id}", new { tenant.Id, tenant.Slug, tenant.Status });
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, TenantStatus from, TenantStatus to, string? reason, string action, TenantsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();
        if (tenant.Status != from) return Results.Conflict();
        if (string.IsNullOrWhiteSpace(reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Informe o motivo."] });

        tenant.Status = to;
        tenant.StatusReason = reason.Trim();
        tenant.SuspendedAt = to == TenantStatus.Suspended ? clock.GetUtcNow() : null;
        tenant.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry(action, ResourceType: "tenant", ResourceId: tenant.Id.ToString()), ct);
        return Results.NoContent();
    }
}

public sealed record CreateTenantRequest(
    string Slug, string DisplayName, string FirstDirectorEmail, string FirstDirectorName, Guid? NetworkId, string? InstitutionalCode);

public sealed record CreateNetworkRequest(string Name, NetworkKind Kind, string? Uf, string? Municipality);

public sealed record TenantNetworkRequest(Guid? NetworkId);

public sealed record ReasonRequest(string? Reason);

public sealed record TenantSummary(
    Guid Id, string Slug, string DisplayName, Uri Url, string? Network, TenantStatus Status, string? StatusReason, string? SchemaVersion, DateTimeOffset CreatedAt,
    Guid? NetworkId = null);

public sealed record TenantDetails(
    Guid Id, string Slug, string DisplayName, string? Network, string? InstitutionalCode, TenantStatus Status, string? StatusReason,
    string? SchemaVersion, DateTimeOffset CreatedAt, IReadOnlyList<StepView> Steps);

public sealed record StepView(string Step, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? Error);
