using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Telas de auditoria: escola (só Diretor, P9) e plataforma (só SUPER_ADMIN). Nunca conteúdo, só o fato.</summary>
[Collection(SineduTestGroup.Name)]
public class AuditScreenTests(SineduFixture fx)
{
    [Fact]
    public async Task Diretor_ve_quem_acessou_registro_protegido_de_um_aluno()
    {
        var sample = await SchoolData.EnsureAsync(fx, SineduFixture.SchoolA);
        var pedagogue = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Pedagogue]);
        await using (var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSineduPostgres(await fx.SchoolConnectionAsync(SineduFixture.SchoolA, owner: true), AuditDbContext.Schema).Options))
        {
            audit.Events.Add(new AuditEvent { OccurredAt = DateTimeOffset.UtcNow, UserId = pedagogue.UserId, StudentId = sample.StudentInX, Action = "health_viewed", Result = AuditResult.Allowed });
            audit.Events.Add(new AuditEvent { OccurredAt = DateTimeOffset.UtcNow, UserId = pedagogue.UserId, Action = "login_succeeded", Result = AuditResult.Allowed });
            await audit.SaveChangesAsync();
        }

        using var director = fx.SchoolClient(SineduFixture.SchoolA, await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]));
        var page = await director.GetFromJsonAsync<JsonElement>($"/api/audit?category=protegidos&studentId={sample.StudentInX}");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var item = Assert.Single(items, i => i.GetProperty("user").GetProperty("id").GetGuid() == pedagogue.UserId);
        Assert.Equal("health_viewed", item.GetProperty("action").GetString());
        Assert.Equal(sample.StudentInXName, item.GetProperty("student").GetProperty("name").GetString());
        Assert.All(items, i => Assert.NotEqual("login_succeeded", i.GetProperty("action").GetString()));

        // Só o fato: nenhum campo de conteúdo na resposta.
        var props = item.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(["action", "id", "ip", "isSupportSession", "occurredAt", "result", "student", "user"], props.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Auditoria_da_plataforma_so_para_super_admin()
    {
        var (_, support) = await fx.PanelClientAsync(Roles.Support);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri("/api/audit", UriKind.Relative))).StatusCode);
        using var anonymous = fx.PlatformClient("painel.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/audit", UriKind.Relative))).StatusCode);

        var (adminId, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var name = $"Rede Auditada {Guid.NewGuid():N}"[..25];
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/networks", new { name, kind = "Municipal" })).StatusCode);

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=network_created");
        Assert.Contains(page.GetProperty("items").EnumerateArray(), i => i.GetProperty("user").GetProperty("id").GetGuid() == adminId);

        var schools = await admin.GetFromJsonAsync<JsonElement>("/api/audit?category=escolas");
        Assert.All(schools.GetProperty("items").EnumerateArray(), i => Assert.StartsWith("tenant_", i.GetProperty("action").GetString()!.Replace("metrics_collected", "tenant_", StringComparison.Ordinal), StringComparison.Ordinal));
    }
}
