using Microsoft.Extensions.DependencyInjection;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Identity;

namespace Sinedu.IntegrationTests;

/// <summary>Instalação nova: o primeiro SUPER_ADMIN é convidado uma única vez (comando create-super-admin).</summary>
[Collection(SineduTestGroup.Name)]
public class PlatformBootstrapTests(SineduFixture fx)
{
    [Fact]
    public async Task Primeiro_super_admin_so_pode_ser_criado_uma_vez()
    {
        var email = $"admin-{Guid.NewGuid():N}@plataforma.local";
        await using (var scope = fx.Platform.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountService>().InviteFirstSuperAdminAsync(email, "Administração de teste", CancellationToken.None);
        }

        Assert.Contains(fx.Email.Sent, m => m.To == email && m.TextBody.Contains("convite?token=", StringComparison.Ordinal));

        await using (var scope = fx.Platform.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<AccountService>().InviteFirstSuperAdminAsync($"outro-{Guid.NewGuid():N}@plataforma.local", "Outra pessoa", CancellationToken.None));
        }
    }
}
