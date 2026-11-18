using System.Net;
using System.Net.Http.Json;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Matriz de permissões (.doc/arquitetura/matriz-de-permissoes.md) aplicada aos endpoints existentes.
/// Cada endpoint novo entra aqui com todos os papéis + anônimo.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class PermissionMatrixTests(SineduFixture fx)
{
    public static TheoryData<string, string?, HttpStatusCode> Matrix => new()
    {
        // GET /api/me — qualquer membro
        { "/api/me", null, HttpStatusCode.Unauthorized },
        { "/api/me", Roles.Director, HttpStatusCode.OK },
        { "/api/me", Roles.Teacher, HttpStatusCode.OK },
        { "/api/me", Roles.ReadOnly, HttpStatusCode.OK },

        // GET /api/access/members — Diretor e Admin técnico
        { "/api/access/members", null, HttpStatusCode.Unauthorized },
        { "/api/access/members", Roles.Director, HttpStatusCode.OK },
        { "/api/access/members", Roles.TechAdmin, HttpStatusCode.OK },
        { "/api/access/members", Roles.Pedagogue, HttpStatusCode.Forbidden },
        { "/api/access/members", Roles.Teacher, HttpStatusCode.Forbidden },
        { "/api/access/members", Roles.ReadOnly, HttpStatusCode.Forbidden },
        { "/api/access/members", Roles.PlatformSupport, HttpStatusCode.Forbidden },

        // GET /api/security/step-up — só Diretor/Pedagogo (base dos módulos protegidos)
        { "/api/security/step-up", null, HttpStatusCode.Unauthorized },
        { "/api/security/step-up", Roles.Director, HttpStatusCode.NoContent },
        { "/api/security/step-up", Roles.Pedagogue, HttpStatusCode.NoContent },
        { "/api/security/step-up", Roles.TechAdmin, HttpStatusCode.Forbidden },
        { "/api/security/step-up", Roles.Teacher, HttpStatusCode.Forbidden },
        { "/api/security/step-up", Roles.ReadOnly, HttpStatusCode.Forbidden },
        { "/api/security/step-up", Roles.PlatformSupport, HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_respeita_a_matriz(string path, string? role, HttpStatusCode expected)
    {
        var session = role is null ? null : await fx.SignInAsync(SineduFixture.SchoolA, [role]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Step_up_vencido_exige_reautenticacao()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director], authAge: TimeSpan.FromMinutes(16));
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        var response = await client.GetAsync(new Uri("/api/security/step-up", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("step_up_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Step_up_sem_mfa_exige_reautenticacao()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Pedagogue], mfa: false);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        var response = await client.GetAsync(new Uri("/api/security/step-up", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Papel_expirado_nao_da_acesso()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.PlatformSupport], roleExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Sessao_revogada_perde_acesso_imediatamente()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);

        var logout = await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Director, Roles.Pedagogue, HttpStatusCode.OK)]
    [InlineData(Roles.Director, Roles.Director, HttpStatusCode.OK)]
    [InlineData(Roles.Director, Roles.Teacher, HttpStatusCode.OK)]
    [InlineData(Roles.TechAdmin, Roles.Teacher, HttpStatusCode.OK)]
    [InlineData(Roles.TechAdmin, Roles.ReadOnly, HttpStatusCode.OK)]
    [InlineData(Roles.TechAdmin, Roles.Pedagogue, HttpStatusCode.Forbidden)]
    [InlineData(Roles.TechAdmin, Roles.Director, HttpStatusCode.Forbidden)]
    [InlineData(Roles.TechAdmin, Roles.TechAdmin, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Director, Roles.PlatformSupport, HttpStatusCode.BadRequest)]
    [InlineData(Roles.Pedagogue, Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, Roles.Teacher, HttpStatusCode.Forbidden)]
    public async Task So_quem_pode_concede_cada_papel(string actor, string role, HttpStatusCode expected)
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [actor]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        var response = await client.PostAsJsonAsync("/api/access/invitations",
            new { email = $"{Guid.NewGuid():N}@escola-a.local", fullName = "Convidado de teste", role });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Concessao_de_papel_sensivel_avisa_os_diretores()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);
        var email = $"{Guid.NewGuid():N}@escola-a.local";

        await client.PostAsJsonAsync("/api/access/invitations", new { email, fullName = "Pedagoga de teste", role = Roles.Pedagogue });

        Assert.Contains(fx.Email.Sent, m => m.TextBody.Contains(email, StringComparison.Ordinal) && m.Subject.StartsWith("Alteração de acesso", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Conta_da_plataforma_nao_recebe_papel_na_escola()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);

        var response = await client.PostAsJsonAsync("/api/access/invitations",
            new { email = "suporte@plataforma.local", fullName = "Suporte", role = Roles.Teacher });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_com_cookie_de_outra_origem_e_bloqueado()
    {
        var session = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolA, session);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "http://atacante.example");

        var response = await client.PostAsJsonAsync("/api/access/invitations",
            new { email = "x@escola-a.local", fullName = "X", role = Roles.Teacher });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
