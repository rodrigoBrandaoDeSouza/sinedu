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

/// <summary>Ocorrências (Fase 5): ciclo de vida, revisões, turma do fato, anexos, ata, permissões e isolamento.</summary>
[Collection(SineduTestGroup.Name)]
public class OccurrenceTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;
    internal static readonly Guid Indisciplina = new("0193a000-0000-7000-8000-000000000001");
    internal static readonly Guid Leve = new("0193a000-0000-7000-8000-000000000101");

    public static TheoryData<string, string?, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string?, HttpStatusCode>();
        foreach (var path in new[] { "/api/occurrences", "/api/occurrences/categories", "/api/communications", "/api/communications/templates" })
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
        var session = role is null ? null : await fx.SignInAsync(A, [role]);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(expected, (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.TechAdmin)]
    [InlineData(Roles.ReadOnly)]
    [InlineData(Roles.PlatformSupport)]
    public async Task Papeis_sem_acesso_nao_criam_nem_leem_ocorrencias(string role)
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var id = await CreateAsync(director, studentId, register: true);

        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [role]));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/occurrences", Body(studentId, register: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/api/occurrences/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Diretor_de_outra_escola_nao_ve_nem_usa_aluno_de_outra_escola()
    {
        var (studentInA, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var id = await CreateAsync(director, studentInA, register: true);

        using var other = fx.SchoolClient(SineduFixture.SchoolB, await fx.SignInAsync(SineduFixture.SchoolB, [Roles.Director]));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/api/occurrences/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/occurrences", Body(studentInA, register: true))).StatusCode);
    }

    /// <summary>Critério de aceite 4: cria, registra providência, gera ata e vê no histórico do aluno.</summary>
    [Fact]
    public async Task Ciclo_completo_com_revisoes_ata_e_historico_do_aluno()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        using var pedagogue = await ClientAsync(Roles.Pedagogue);

        var id = await CreateAsync(director, studentId, register: false);

        // Rascunho só existe para o autor.
        Assert.Equal(HttpStatusCode.NotFound, (await pedagogue.GetAsync(new Uri($"/api/occurrences/{id}", UriKind.Relative))).StatusCode);
        Assert.DoesNotContain(id.ToString(), await pedagogue.GetStringAsync(new Uri($"/api/occurrences?studentId={studentId}", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await director.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/occurrences/{id}/register", new { expectedVersion = 0 })).StatusCode);
        var registered = await pedagogue.GetFromJsonAsync<JsonElement>($"/api/occurrences/{id}");
        Assert.Equal("Registrada", registered.GetProperty("status").GetString());
        Assert.Equal(1, registered.GetProperty("version").GetInt32());

        // Edição de registrada exige justificativa e gera revisão.
        var noReason = await pedagogue.PutAsJsonAsync($"/api/occurrences/{id}", Body(studentId, actions: "Conversa com o aluno", expectedVersion: 1));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var edited = await pedagogue.PutAsJsonAsync($"/api/occurrences/{id}", Body(studentId, actions: "Conversa com o aluno e com a turma", reason: "Inclusão da providência", expectedVersion: 1));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await director.PutAsJsonAsync($"/api/occurrences/{id}", Body(studentId, reason: "Edição concorrente", expectedVersion: 1))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/occurrences/{id}/status", new { status = "EmAcompanhamento", expectedVersion = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await director.PostAsJsonAsync($"/api/occurrences/{id}/status", new { status = "Concluida", expectedVersion = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/occurrences/{id}/status", new { status = "Concluida", reason = "Família compareceu e combinou acompanhamento", expectedVersion = 3 })).StatusCode);

        var done = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{id}");
        Assert.Equal("Concluida", done.GetProperty("status").GetString());
        Assert.Equal(4, done.GetProperty("revisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await director.PutAsJsonAsync($"/api/occurrences/{id}", Body(studentId, reason: "Depois de concluída", expectedVersion: 4))).StatusCode);

        var v1 = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{id}/revisions/1");
        Assert.Equal("Conversa inicial", v1.GetProperty("actionsTaken").GetString());

        var ata = await director.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative));
        Assert.Equal("application/pdf", ata.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await ata.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);

        var history = await pedagogue.GetFromJsonAsync<JsonElement>($"/api/occurrences?studentId={studentId}");
        Assert.Contains(history.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);

        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(A, owner: true), AuditDbContext.Schema).Options);
        var actions = await audit.Events.Where(e => e.ResourceId == id.ToString()).Select(e => e.Action).ToListAsync();
        Assert.Contains("occurrence_registered", actions);
        Assert.Contains("occurrence_concluida", actions);
        Assert.Contains("occurrence_ata_generated", actions);
    }

    [Fact]
    public async Task Registrada_nao_e_excluida_so_cancelada_com_justificativa()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var draft = await CreateAsync(director, studentId, register: false);
        var registered = await CreateAsync(director, studentId, register: true);

        Assert.Equal(HttpStatusCode.NoContent, (await director.DeleteAsync(new Uri($"/api/occurrences/{draft}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await director.DeleteAsync(new Uri($"/api/occurrences/{registered}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await director.PostAsJsonAsync($"/api/occurrences/{registered}/status", new { status = "Cancelada", reason = "curto", expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/occurrences/{registered}/status", new { status = "Cancelada", reason = "Registrada no aluno errado por engano", expectedVersion = 1 })).StatusCode);

        var cancelled = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{registered}");
        Assert.Equal("Cancelada", cancelled.GetProperty("status").GetString());
        Assert.Equal("Registrada no aluno errado por engano", cancelled.GetProperty("cancelReason").GetString());
    }

    /// <summary>Critério de aceite 7: a ocorrência antiga mantém turma e ano mesmo depois da troca de turma.</summary>
    [Fact]
    public async Task Turma_da_data_do_fato_e_preservada_apos_remanejamento()
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        var before = await CreateAsync(director, studentId, register: true, occurredAt: yesterday);
        Assert.Equal(HttpStatusCode.NoContent, (await director.PostAsJsonAsync($"/api/students/{studentId}/move", new { classroomId = sample.ClassroomY })).StatusCode);

        // Registrada depois da troca, mas sobre um fato de ontem: continua na turma antiga.
        var lateReport = await CreateAsync(director, studentId, register: true, occurredAt: yesterday.AddHours(-2));
        var today = await CreateAsync(director, studentId, register: true, occurredAt: DateTimeOffset.UtcNow);

        Assert.Equal(sample.ClassroomX, (await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{before}")).GetProperty("classroomId").GetGuid());
        Assert.Equal(sample.ClassroomX, (await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{lateReport}")).GetProperty("classroomId").GetGuid());
        Assert.Equal(sample.ClassroomY, (await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{today}")).GetProperty("classroomId").GetGuid());

        var byOldClass = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences?classroomId={sample.ClassroomX}&studentId={studentId}");
        Assert.Equal(2, byOldClass.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Fato_no_futuro_ou_sem_matricula_e_recusado()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);

        Assert.Equal(HttpStatusCode.BadRequest, (await director.PostAsJsonAsync("/api/occurrences", Body(studentId, occurredAt: DateTimeOffset.UtcNow.AddDays(2)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await director.PostAsJsonAsync("/api/occurrences", Body(studentId, occurredAt: new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero)))).StatusCode);
    }

    [Fact]
    public async Task Anexo_de_ocorrencia_so_por_formato_permitido_e_download_pela_api()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var id = await CreateAsync(director, studentId, register: true);
        var content = Encoding.ASCII.GetBytes("%PDF-1.4\n% termo fictício\n%%EOF\n");

        var upload = await director.PostAsync(new Uri($"/api/occurrences/{id}/attachments", UriKind.Relative), FileForm(content, "termo.pdf"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachmentId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(content, await director.GetByteArrayAsync(new Uri($"/api/occurrences/{id}/attachments/{attachmentId}", UriKind.Relative)));

        var bad = await director.PostAsync(new Uri($"/api/occurrences/{id}/attachments", UriKind.Relative), FileForm(Encoding.UTF8.GetBytes("MZ executável"), "foto.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Somente_direcao_configura_categorias()
    {
        using var director = await ClientAsync(Roles.Director);
        using var pedagogue = await ClientAsync(Roles.Pedagogue);
        var name = "Uso de celular " + Guid.NewGuid().ToString("N")[..4];

        Assert.Equal(HttpStatusCode.Forbidden, (await pedagogue.PostAsJsonAsync("/api/occurrences/categories", new { name })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await director.PostAsJsonAsync("/api/occurrences/categories", new { name })).StatusCode);
    }

    // ---------- apoio ----------

    private async Task<HttpClient> ClientAsync(string role) => fx.SchoolClient(A, await fx.SignInAsync(A, [role]));

    internal static object Body(
        Guid studentId, bool register = false, DateTimeOffset? occurredAt = null, string actions = "Conversa inicial", string? reason = null, int expectedVersion = 0,
        string description = "Discussão em sala durante a aula de matemática, com interrupção da atividade.") =>
        new
        {
            studentId, occurredAt = occurredAt ?? DateTimeOffset.UtcNow.AddHours(-1), categoryId = Indisciplina, severityId = Leve, location = "Sala 12",
            description, actionsTaken = actions, register, reason, expectedVersion,
        };

    internal static async Task<Guid> CreateAsync(HttpClient client, Guid studentId, bool register, DateTimeOffset? occurredAt = null, string? description = null)
    {
        var response = await client.PostAsJsonAsync("/api/occurrences", description is null ? Body(studentId, register, occurredAt) : Body(studentId, register, occurredAt, description: description));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent FileForm(byte[] content, string name) => new() { { new ByteArrayContent(content), "file", name } };
}
