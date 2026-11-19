using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Students;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Responsáveis na tela do aluno (F2-06): só Direção e Pedagogia cadastram, corrigem e desvinculam; nunca mescla
/// pessoas só pelo nome; desvincular não apaga a pessoa; aluno de outra escola → 404.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class GuardianTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Director, HttpStatusCode.Created)]
    [InlineData(Roles.Pedagogue, HttpStatusCode.Created)]
    [InlineData(Roles.TechAdmin, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.ReadOnly, HttpStatusCode.Forbidden)]
    [InlineData(Roles.PlatformSupport, HttpStatusCode.Forbidden)]
    public async Task So_Direcao_e_Pedagogia_cadastram_responsavel(string? role, HttpStatusCode expected)
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, role is null ? null : await fx.SignInAsync(A, [role]));

        var response = await client.PostAsJsonAsync($"/api/students/{sample.StudentInY}/guardians", NewGuardian("MARIA FICTÍCIA DE TESTE"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Cadastra_corrige_e_desvincula_sem_apagar_a_pessoa()
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Pedagogue]));

        var created = await client.PostAsJsonAsync($"/api/students/{sample.StudentInX}/guardians",
            NewGuardian("JOANA FICTÍCIA", email: "joana@exemplo.local", authorized: true, basis: "Termo assinado na matrícula 2026"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var guardianId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var guardian = await GuardianOnStudentAsync(client, sample.StudentInX, guardianId);
        Assert.Equal("JOANA FICTÍCIA", guardian.GetProperty("name").GetString());
        Assert.True(guardian.GetProperty("contactAuthorized").GetBoolean());
        Assert.True(guardian.GetProperty("isPrimaryContact").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/students/{sample.StudentInX}/guardians/{guardianId}",
            new { fullName = "JOANA FICTÍCIA SOUZA", relationship = "Avos" })).StatusCode);
        guardian = await GuardianOnStudentAsync(client, sample.StudentInX, guardianId);
        Assert.Equal("JOANA FICTÍCIA SOUZA", guardian.GetProperty("name").GetString());
        Assert.Equal("Avos", guardian.GetProperty("relationship").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(new Uri($"/api/students/{sample.StudentInX}/guardians/{guardianId}", UriKind.Relative))).StatusCode);
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/students/{sample.StudentInX}");
        Assert.DoesNotContain(details.GetProperty("guardians").EnumerateArray(), g => g.GetProperty("id").GetGuid() == guardianId);

        await using var db = await StudentsAsync();
        Assert.True(await db.Guardians.AnyAsync(g => g.Id == guardianId));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(new Uri($"/api/students/{sample.StudentInX}/guardians/{guardianId}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Mesmo_nome_vira_duas_pessoas_e_autorizar_exige_fundamento()
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        var name = $"HOMÔNIMO FICTÍCIO {Guid.NewGuid():N}"[..30];

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/students/{sample.StudentInX}/guardians", NewGuardian(name))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/students/{sample.StudentInY}/guardians", NewGuardian(name))).StatusCode);
        await using (var db = await StudentsAsync())
        {
            Assert.Equal(2, await db.Guardians.CountAsync(g => g.FullName == name));
        }

        var noBasis = await client.PostAsJsonAsync($"/api/students/{sample.StudentInX}/guardians", NewGuardian("SEM FUNDAMENTO", authorized: true));
        Assert.Equal(HttpStatusCode.BadRequest, noBasis.StatusCode);
        Assert.Contains("contactBasis", await noBasis.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var noRelationship = await client.PostAsJsonAsync($"/api/students/{sample.StudentInX}/guardians", new { fullName = "SEM PARENTESCO" });
        Assert.Equal(HttpStatusCode.BadRequest, noRelationship.StatusCode);
    }

    [Fact]
    public async Task Aluno_de_outra_escola_responde_404()
    {
        var other = await SchoolData.EnsureAsync(fx, SineduFixture.SchoolB);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/students/{other.StudentInX}/guardians", NewGuardian("OUTRA ESCOLA"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/students/{other.StudentInX}/guardians/{Guid.NewGuid()}", new { fullName = "X X X", relationship = "Mae" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(new Uri($"/api/students/{other.StudentInX}/guardians/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
    }

    private static object NewGuardian(string name, string? email = null, bool authorized = false, string? basis = null) => new
    {
        fullName = name,
        relationship = "Mae",
        phone = "(00) 90000-0000",
        email,
        isPrimaryContact = true,
        contactAuthorized = authorized,
        contactBasis = basis,
    };

    private static async Task<JsonElement> GuardianOnStudentAsync(HttpClient client, Guid studentId, Guid guardianId)
    {
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}");
        return details.GetProperty("guardians").EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == guardianId);
    }

    private async Task<StudentsDbContext> StudentsAsync() =>
        new(new DbContextOptionsBuilder<StudentsDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(A, owner: true), StudentsDbContext.Schema).Options);
}
