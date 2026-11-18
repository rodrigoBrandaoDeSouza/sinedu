using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Indicadores (Fase 6, critério 6): o que conta, denominador, turma histórica, supressão e permissões.</summary>
[Collection(SineduTestGroup.Name)]
public class AnalyticsTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    public static TheoryData<string, string?, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string?, HttpStatusCode>();
        foreach (var path in new[] { "/api/analytics/options", "/api/analytics/students?from=2026-02-02&to=2026-03-01", "/api/analytics/export?from=2026-02-02&to=2026-03-01" })
        {
            data.Add(path, null, HttpStatusCode.Unauthorized);
            data.Add(path, Roles.Director, HttpStatusCode.OK);
            data.Add(path, Roles.Pedagogue, HttpStatusCode.OK);
            data.Add(path, Roles.TechAdmin, HttpStatusCode.Forbidden);
            data.Add(path, Roles.Teacher, HttpStatusCode.Forbidden);
            data.Add(path, Roles.ReadOnly, HttpStatusCode.Forbidden);
            data.Add(path, Roles.PlatformSupport, HttpStatusCode.Forbidden);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_respeita_a_matriz(string path, string? role, HttpStatusCode expected)
    {
        await SchoolData.EnsureAsync(fx, A);
        var session = role is null ? null : await fx.SignInAsync(A, [role]);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(expected, (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
    }

    /// <summary>Critério 6: totais, alunos distintos, categoria, situação e taxa por 100 alunos, contando só o que vale.</summary>
    [Fact]
    public async Task Conta_so_ocorrencias_formalizadas_com_denominador_da_turma()
    {
        var (classroom, students, director) = await ClassroomWithStudentsAsync(6);
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var o1 = await OccurrenceTests.CreateAsync(director, students[0], register: true, occurredAt: now);
        await OccurrenceTests.CreateAsync(director, students[0], register: true, occurredAt: now.AddHours(-1));
        await OccurrenceTests.CreateAsync(director, students[1], register: true, occurredAt: now.AddHours(-2));
        var concluded = await OccurrenceTests.CreateAsync(director, students[2], register: true, occurredAt: now.AddDays(-1));
        await director.PostAsJsonAsync($"/api/occurrences/{concluded}/status", new { status = "Concluida", reason = "Resolvido com a família", expectedVersion = 1 });
        await OccurrenceTests.CreateAsync(director, students[3], register: false, occurredAt: now); // rascunho: não conta
        var cancelled = await OccurrenceTests.CreateAsync(director, students[4], register: true, occurredAt: now);
        await director.PostAsJsonAsync($"/api/occurrences/{cancelled}/status", new { status = "Cancelada", reason = "Registrada por engano no aluno errado", expectedVersion = 1 });

        var d = await DashboardAsync(director, classroom);

        Assert.Equal(6, d.GetProperty("enrolled").GetInt32());
        Assert.Equal(4, d.GetProperty("occurrences").GetInt32());
        Assert.Equal(3, d.GetProperty("studentsWithOccurrence").GetInt32());
        Assert.Equal(3, d.GetProperty("status").GetProperty("registered").GetInt32());
        Assert.Equal(1, d.GetProperty("status").GetProperty("concluded").GetInt32());
        var row = Assert.Single(d.GetProperty("byClassroom").EnumerateArray());
        Assert.Equal(66.7, row.GetProperty("ratePer100").GetDouble());
        Assert.Equal(4, d.GetProperty("byCategory")[0].GetProperty("value").GetInt32());
        Assert.Contains(d.GetProperty("followUps").EnumerateArray(), f => f.GetProperty("id").GetGuid() == o1);
        Assert.Equal(4, d.GetProperty("weekly").EnumerateArray().Sum(w => w.GetProperty("current").ValueKind == JsonValueKind.Number ? w.GetProperty("current").GetInt32() : 0));
    }

    [Fact]
    public async Task Ocorrencia_fica_na_turma_da_epoca_e_aluno_remanejado_conta_nas_duas()
    {
        var (from, students, director) = await ClassroomWithStudentsAsync(3);
        var to = await SchoolData.NewClassroomAsync(fx, A, "W" + Guid.NewGuid().ToString("N")[..3]);
        await OccurrenceTests.CreateAsync(director, students[0], register: true, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/students/{students[0]}/move", new { classroomId = to })).StatusCode);

        var old = await DashboardAsync(director, from);
        var @new = await DashboardAsync(director, to);

        Assert.Equal(1, old.GetProperty("occurrences").GetInt32());
        Assert.Equal(3, old.GetProperty("enrolled").GetInt32());
        Assert.Equal(0, @new.GetProperty("occurrences").GetInt32());
        Assert.Equal(1, @new.GetProperty("enrolled").GetInt32());
    }

    [Fact]
    public async Task Exportacao_so_tem_agregados_com_supressao_de_contagens_pequenas()
    {
        var (classroom, students, director) = await ClassroomWithStudentsAsync(7);
        var marker = "FATO-EXPORT-" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await OccurrenceTests.CreateAsync(director, students[i], register: true, occurredAt: DateTimeOffset.UtcNow.AddHours(-1 - i), description: $"{marker} descrição de teste do fato.");
        }

        var csv = await director.GetAsync(new Uri($"/api/analytics/export?format=csv&{Range()}&classroomId={classroom}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Contains("Ocorrências registradas;<5", text, StringComparison.Ordinal);
        Assert.Contains("Alunos matriculados;7", text, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CAIO FICTÍCIO", text, StringComparison.Ordinal);
        Assert.Contains(";—", text, StringComparison.Ordinal); // taxa omitida quando a contagem é suprimida

        var pdf = await director.GetAsync(new Uri($"/api/analytics/export?format=pdf&{Range()}&classroomId={classroom}", UriKind.Relative));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(A, owner: true), AuditDbContext.Schema).Options);
        Assert.True(await audit.Events.AnyAsync(e => e.Action == "analytics_exported"));
    }

    [Fact]
    public async Task Csv_neutraliza_formulas_em_nomes_digitados_pela_escola()
    {
        var (classroom, students, director) = await ClassroomWithStudentsAsync(1);
        var name = "=HYPERLINK(\"http://atacante.example\") " + Guid.NewGuid().ToString("N")[..4];
        var created = await director.PostAsJsonAsync("/api/occurrences/categories", new { name });
        var categoryId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var occurrence = await director.PostAsJsonAsync("/api/occurrences", new
        {
            studentId = students[0], occurredAt = DateTimeOffset.UtcNow.AddHours(-1), categoryId, description = "Fato de teste para a exportação.", register = true,
        });
        Assert.Equal(HttpStatusCode.Created, occurrence.StatusCode);

        var text = await director.GetStringAsync(new Uri($"/api/analytics/export?format=csv&{Range()}&classroomId={classroom}", UriKind.Relative));

        Assert.Contains("'=HYPERLINK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=HYPERLINK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\"=HYPERLINK", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocorrencias_por_aluno_em_ordem_alfabetica_e_so_para_a_equipe_pedagogica()
    {
        var (classroom, students, director) = await ClassroomWithStudentsAsync(2, ["ZÉLIA FICTÍCIA", "ARTUR FICTÍCIO"]);
        await OccurrenceTests.CreateAsync(director, students[0], register: true);
        await OccurrenceTests.CreateAsync(director, students[0], register: true);
        await OccurrenceTests.CreateAsync(director, students[1], register: true);

        var result = await director.GetFromJsonAsync<JsonElement>($"/api/analytics/students?{Range()}&classroomId={classroom}");
        var rows = result.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["ARTUR FICTÍCIO", "ZÉLIA FICTÍCIA"], rows.Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(2, rows[1].GetProperty("occurrences").GetInt32());
    }

    [Fact]
    public async Task Escola_b_nao_ve_turma_nem_alunos_da_escola_a()
    {
        var (classroom, _, director) = await ClassroomWithStudentsAsync(2);
        director.Dispose();
        using var other = fx.SchoolClient(SineduFixture.SchoolB, await fx.SignInAsync(SineduFixture.SchoolB, [Roles.Director]));
        await SchoolData.EnsureAsync(fx, SineduFixture.SchoolB);

        var d = await DashboardAsync(other, classroom);
        Assert.Equal(0, d.GetProperty("enrolled").GetInt32());
        Assert.Empty(d.GetProperty("byClassroom").EnumerateArray());
    }

    // ---------- apoio ----------

    private static string Range() =>
        $"from={DateTime.UtcNow.AddDays(-6):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}";

    private static async Task<JsonElement> DashboardAsync(HttpClient client, Guid classroomId)
    {
        var response = await client.GetAsync(new Uri($"/api/analytics/dashboard?{Range()}&classroomId={classroomId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(Guid Classroom, List<Guid> Students, HttpClient Director)> ClassroomWithStudentsAsync(int count, string[]? names = null)
    {
        var classroom = await SchoolData.NewClassroomAsync(fx, A, "Z" + Guid.NewGuid().ToString("N")[..3]);
        var students = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            students.Add(await SchoolData.NewStudentInAsync(fx, A, classroom, names?[i] ?? $"CAIO FICTÍCIO {i}"));
        }

        return (classroom, students, fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director])));
    }
}
