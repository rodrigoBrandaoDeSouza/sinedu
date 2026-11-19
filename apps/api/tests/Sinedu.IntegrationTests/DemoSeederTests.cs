using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.ProtectedRecords;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.IntegrationTests;

/// <summary>seed-demo: gera dados fictícios pelos serviços reais e pode ser repetido sem duplicar.</summary>
[Collection(SineduTestGroup.Name)]
public class DemoSeederTests(SineduFixture fx)
{
    [Fact]
    public async Task Gera_dados_ficticios_e_repetir_nao_duplica()
    {
        const string slug = "escola-seed-demo";
        await fx.ProvisionExtraAsync(slug, "Escola Seed Demo (teste)");
        await SchoolData.EnsureAsync(fx, slug);
        await fx.SignInAsync(slug, [Roles.Director]);
        var year = DateTime.UtcNow.Year;

        await Sinedu.Tenant.Host.Dev.DemoSeeder.RunAsync(fx.School.Services, slug, year);
        var first = await CountsAsync(slug, year);
        await Sinedu.Tenant.Host.Dev.DemoSeeder.RunAsync(fx.School.Services, slug, year);
        var second = await CountsAsync(slug, year);

        Assert.True(first.Occurrences > 10, $"ocorrências: {first.Occurrences}");
        Assert.True(first.Health > 0);
        Assert.True(first.Protection > 0);
        Assert.True(first.Staff >= 15);
        Assert.Equal(1, first.NextYears);
        Assert.Equal(first, second);
        Assert.Contains(fx.Email.Sent, m => m.To.EndsWith("@demo.invalid", StringComparison.Ordinal));
    }

    private sealed record Counts(int Occurrences, int Health, int Protection, int Staff, int NextYears);

    private async Task<Counts> CountsAsync(string slug, int year)
    {
        var cs = await fx.SchoolConnectionAsync(slug, owner: true);
        await using var occ = new OccurrencesDbContext(new DbContextOptionsBuilder<OccurrencesDbContext>().UseSineduPostgres(cs, OccurrencesDbContext.Schema).Options);
        await using var prot = new ProtectedDbContext(new DbContextOptionsBuilder<ProtectedDbContext>().UseSineduPostgres(cs, ProtectedDbContext.Schema).Options);
        await using var school = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSineduPostgres(cs, SchoolDbContext.Schema).Options);
        return new Counts(
            await occ.Occurrences.CountAsync(), await prot.HealthRecords.CountAsync(), await prot.ProtectionRecords.CountAsync(),
            await school.Staff.CountAsync(), await school.Years.CountAsync(y => y.Year == year + 1));
    }
}
