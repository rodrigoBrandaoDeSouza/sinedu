using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Escopo de alunos por papel (matriz §3 + P2/P6/D10) e isolamento entre escolas.</summary>
[Collection(SineduTestGroup.Name)]
public class StudentScopeTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    public static TheoryData<string, string?, HttpStatusCode> Matrix => new()
    {
        { "/api/students", null, HttpStatusCode.Unauthorized },
        { "/api/students", Roles.Director, HttpStatusCode.OK },
        { "/api/students", Roles.Pedagogue, HttpStatusCode.OK },
        { "/api/students", Roles.Teacher, HttpStatusCode.OK },
        { "/api/students", Roles.ReadOnly, HttpStatusCode.OK },
        { "/api/students", Roles.PlatformSupport, HttpStatusCode.OK },
        { "/api/students", Roles.TechAdmin, HttpStatusCode.Forbidden },
        { "/api/school/classrooms", Roles.TechAdmin, HttpStatusCode.OK },
        { "/api/school/classrooms", Roles.Teacher, HttpStatusCode.OK },
        { "/api/school/years", Roles.ReadOnly, HttpStatusCode.OK },
        { "/api/school/staff", Roles.Director, HttpStatusCode.OK },
        { "/api/school/staff", Roles.Pedagogue, HttpStatusCode.OK },
        { "/api/school/staff", Roles.TechAdmin, HttpStatusCode.OK },
        { "/api/school/staff", Roles.Teacher, HttpStatusCode.Forbidden },
        { "/api/school/staff", Roles.ReadOnly, HttpStatusCode.Forbidden },
        { "/api/students/classroom-counts", Roles.TechAdmin, HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoints_de_cadastro_respeitam_a_matriz(string path, string? role, HttpStatusCode expected)
    {
        await SchoolData.EnsureAsync(fx, A);
        var session = role is null ? null : await fx.SignInAsync(A, [role]);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(expected, (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Professor_ve_so_alunos_das_proprias_turmas()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        var session = await fx.SignInAsync(A, [Roles.Teacher]);
        await SchoolData.AssignTeacherAsync(fx, A, session.UserId, data.ClassroomX);
        using var client = fx.SchoolClient(A, session);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/students");
        var ids = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(data.StudentInX, ids);
        Assert.DoesNotContain(data.StudentInY, ids);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri($"/api/students/{data.StudentInX}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/students/{data.StudentInY}", UriKind.Relative))).StatusCode);

        var classrooms = await client.GetFromJsonAsync<JsonElement>("/api/school/classrooms");
        Assert.Equal([data.ClassroomX], classrooms.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray());
    }

    [Fact]
    public async Task Professor_sem_turma_nao_ve_nenhum_aluno()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Teacher]));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/students");

        Assert.Equal(0, list.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/students/{data.StudentInX}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Professor_ve_responsaveis_mas_nao_endereco()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        var session = await fx.SignInAsync(A, [Roles.Teacher]);
        await SchoolData.AssignTeacherAsync(fx, A, session.UserId, data.ClassroomX);
        using var client = fx.SchoolClient(A, session);

        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{data.StudentInX}");

        Assert.True(student.TryGetProperty("guardians", out var guardians));
        Assert.False(guardians[0].TryGetProperty("document", out _));
        Assert.False(student.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task Consulta_ve_so_turmas_concedidas_e_nunca_responsaveis()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        var session = await fx.SignInAsync(A, [Roles.ReadOnly]);
        await SchoolData.GrantReadOnlyAsync(fx, A, session.UserId, data.ClassroomY);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/students/{data.StudentInX}", UriKind.Relative))).StatusCode);
        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{data.StudentInY}");

        Assert.False(student.TryGetProperty("guardians", out _));
        Assert.False(student.TryGetProperty("address", out _));
        Assert.False(student.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task Direcao_ve_cadastro_completo()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));

        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{data.StudentInY}");

        Assert.Equal("RUA FICTÍCIA, 100", student.GetProperty("address").GetString());
        Assert.Equal("000.000.000-00", student.GetProperty("guardians")[0].GetProperty("document").GetString());
        Assert.True(student.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task Painel_do_aluno_nunca_tem_campos_de_saude_ou_protecao()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));

        var json = await client.GetStringAsync(new Uri($"/api/students/{data.StudentInX}", UriKind.Relative));

        Assert.DoesNotMatch("(?i)sa[uú]de|health|prote[cç]|protection|alerg|medic|sigilo", json);
    }

    [Fact]
    public async Task Aluno_de_uma_escola_nao_aparece_em_outra()
    {
        var dataA = await SchoolData.EnsureAsync(fx, A);
        await SchoolData.EnsureAsync(fx, SineduFixture.SchoolB);
        using var inB = fx.SchoolClient(SineduFixture.SchoolB, await fx.SignInAsync(SineduFixture.SchoolB, [Roles.Director]));

        Assert.Equal(HttpStatusCode.NotFound, (await inB.GetAsync(new Uri($"/api/students/{dataA.StudentInX}", UriKind.Relative))).StatusCode);
        var list = await inB.GetFromJsonAsync<JsonElement>($"/api/students?query={Uri.EscapeDataString("ÁLVARO")}");
        Assert.All(list.GetProperty("items").EnumerateArray(), i => Assert.DoesNotContain(A.ToUpperInvariant(), i.GetProperty("name").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Busca_ignora_acentos_e_maiusculas()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/students?query=alvaro%20ficticio");

        Assert.Contains(data.StudentInX, list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Remanejar_preserva_o_historico_de_matriculas()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Pedagogue]));
        var created = await (await client.PostAsJsonAsync("/api/students", new
        {
            fullName = "CARLA FICTÍCIA REMANEJADA", classroomId = data.ClassroomX, guardians = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var move = await client.PostAsJsonAsync($"/api/students/{id}/move", new { classroomId = data.ClassroomY });
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);

        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{id}");
        var enrollments = student.GetProperty("enrollments").EnumerateArray().ToList();
        Assert.Equal(2, enrollments.Count);
        Assert.Contains(enrollments, e => e.GetProperty("status").GetString() == "Remanejada" && e.GetProperty("classroomId").GetGuid() == data.ClassroomX);
        Assert.Equal(data.ClassroomY, student.GetProperty("classroom").GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.ReadOnly)]
    [InlineData(Roles.TechAdmin)]
    public async Task So_direcao_e_pedagogia_cadastram_alunos(string role)
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [role]));

        var response = await client.PostAsJsonAsync("/api/students", new { fullName = "NÃO DEVERIA", classroomId = data.ClassroomX });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Codigo_institucional_nao_se_repete_na_escola()
    {
        var data = await SchoolData.EnsureAsync(fx, A);
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        var body = new { fullName = "DUPLICADO FICTÍCIO", institutionalCode = "1234567890", classroomId = data.ClassroomX };

        await client.PostAsJsonAsync("/api/students", body);
        var second = await client.PostAsJsonAsync("/api/students", body);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
