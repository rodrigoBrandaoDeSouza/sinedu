using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.IntegrationTests;

/// <summary>
/// P-12: desbloqueio e reinício de MFA feitos no painel entram também na auditoria da escola da pessoa — e só dela,
/// uma vez só, sem conteúdo.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class PlatformAccountEventsTests(SineduFixture fx)
{
    [Fact]
    public async Task Reset_de_mfa_e_desbloqueio_aparecem_na_auditoria_da_escola_da_pessoa()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Teacher]);
        var userId = session.UserId;
        await CreatePlatformAccountAsync(userId, SineduFixture.SchoolA);

        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/users/{userId}/reset-mfa", new { reason = "Perdeu o celular e confirmou por telefone" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync(new Uri($"/api/accounts/users/{userId}/unlock", UriKind.Relative), null)).StatusCode);

        // A escola sincroniza duas vezes: nada duplica.
        Assert.Equal(2, await SyncAsync(SineduFixture.SchoolA));
        Assert.Equal(0, await SyncAsync(SineduFixture.SchoolA));
        Assert.Equal(0, await SyncAsync(SineduFixture.SchoolB));

        await using var audit = await AuditAsync(SineduFixture.SchoolA);
        var events = await audit.Events.AsNoTracking().Where(e => e.UserId == userId && e.ResourceType == PlatformAccountEventsSync.ResourceType).ToListAsync();
        Assert.Equal(["platform_account_unlocked", "platform_mfa_reset"], events.Select(e => e.Action).Order(StringComparer.Ordinal).ToArray());

        await using var other = await AuditAsync(SineduFixture.SchoolB);
        Assert.False(await other.Events.AnyAsync(e => e.UserId == userId));
    }

    private async Task<int> SyncAsync(string slug)
    {
        await using var scope = fx.School.Services.CreateAsyncScope();
        var resolution = await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        scope.ServiceProvider.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
        return await scope.ServiceProvider.GetRequiredService<PlatformAccountEventsSync>().SyncAsync(CancellationToken.None);
    }

    private async Task CreatePlatformAccountAsync(Guid userId, string slug)
    {
        await using var tenants = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        var tenant = await tenants.Tenants.SingleAsync(t => t.Slug == slug);
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSineduPostgres(fx.PlatformConnectionString, IdentityDbContext.Schema).UseOpenIddict().Options);
        identity.Users.Add(new UserAccount
        {
            Id = userId, Email = $"{userId:N}@{slug}.local", FullName = "Professora Fictícia", Status = AccountStatus.Active, Kind = AccountKind.School,
            MfaEnabled = true, SecurityStamp = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
        });
        identity.Memberships.Add(new TenantMembership
        {
            Id = Guid.CreateVersion7(), UserId = userId, TenantId = tenant.Id, TenantSlug = tenant.Slug, TenantName = tenant.DisplayName,
            Active = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await identity.SaveChangesAsync();
    }

    private async Task<AuditDbContext> AuditAsync(string slug) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(slug, owner: true), AuditDbContext.Schema).Options);
}
