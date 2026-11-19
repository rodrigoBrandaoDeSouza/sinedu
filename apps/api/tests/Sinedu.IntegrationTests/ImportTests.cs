using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Importação assistida (spec §7.1) com a planilha ANONIMIZADA no formato real (.doc/amostras).
/// Critérios de aceite 1 (importar), 2 (reimportar sem duplicar) e 16 (conflito pede revisão).
/// </summary>
[Collection(SineduTestGroup.Name)]
public class ImportTests(SineduFixture fx)
{
    private const string C = SineduFixture.SchoolC;
    private static readonly SemaphoreSlim YearLock = new(1, 1);
    private static bool _yearCreated;

    [Fact]
    public async Task Planilha_real_anonimizada_importa_reimporta_e_nao_duplica()
    {
        using var client = await DirectorAsync();

        // 1) Envio: abas e mapeamento sugerido pelo formato real.
        var batch = await UploadAsync(client, SamplePath(), "alunos-2026-anonimizado.xlsx");
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{batch}?sheet=Original");
        var mapping = details.GetProperty("mapping");
        Assert.Equal(1, mapping.GetProperty("Code").GetInt32());            // CGM: coluna sem cabeçalho
        Assert.Equal(0, mapping.GetProperty("CallNumber").GetInt32());      // nº de chamada: cabeçalho numérico
        Assert.Equal(2, mapping.GetProperty("Name").GetInt32());
        Assert.Equal(3, mapping.GetProperty("Classroom").GetInt32());
        Assert.Equal(8, mapping.GetProperty("GuardianDocument").GetInt32()); // CPF real, não o checklist "CPF DO RESPONSAVEL"

        // 2) Prévia.
        var preview = await MapAsync(client, batch, "Original", 1, mapping);
        Assert.Equal(1031, preview.GetProperty("new").GetInt32());
        Assert.Equal(1, preview.GetProperty("conflict").GetInt32());        // CGM repetido no arquivo real
        Assert.Equal(34, preview.GetProperty("newClassrooms").GetInt32());

        // 3) Conflito bloqueia a confirmação até ser revisado (critério 16).
        var blocked = await client.PostAsync(new Uri($"/api/imports/{batch}/confirm", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var pending = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{batch}/rows?pendingOnly=true");
        var conflict = pending.GetProperty("items")[0];
        Assert.Contains("CGM repetido", conflict.GetProperty("errors")[0].GetString(), StringComparison.Ordinal);
        var decision = await client.PostAsJsonAsync($"/api/imports/{batch}/rows/{conflict.GetProperty("id").GetGuid()}/decision", new { decision = "Skip" });
        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);

        // 4) Confirmação (critério 1).
        var result = await (await client.PostAsync(new Uri($"/api/imports/{batch}/confirm", UriKind.Relative), null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1031, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());

        var classrooms = await client.GetFromJsonAsync<JsonElement>("/api/school/classrooms");
        Assert.Equal(34, classrooms.GetArrayLength());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/students?pageSize=5");
        Assert.True(list.GetProperty("total").GetInt32() >= 1031);

        // 5) Reimportar o mesmo arquivo não duplica nada (critério 2).
        var again = await UploadAsync(client, SamplePath(), "alunos-2026-anonimizado.xlsx");
        var preview2 = await MapAsync(client, again, "Original", 1, mapping);
        Assert.Equal(0, preview2.GetProperty("new").GetInt32());
        Assert.Equal(1031, preview2.GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task Mesmo_nome_sem_cgm_e_so_sugestao_nunca_uniao_automatica()
    {
        using var client = await DirectorAsync();
        await ImportCsvAsync(client, "CGM;Nome do Aluno;Turma\n5550001001;MARINA FICTÍCIA DUPLICADA;6º A Manhã\n");

        var batch = await UploadCsvAsync(client, "Nome do Aluno;Turma\nmarina ficticia duplicada;6º B Tarde\n");
        var preview = await MapAsync(client, batch, "CSV", 1, Map(("Name", 0), ("Classroom", 1)));
        Assert.Equal(1, preview.GetProperty("possibleDuplicate").GetInt32());
        Assert.Equal(0, preview.GetProperty("new").GetInt32());

        var row = (await client.GetFromJsonAsync<JsonElement>($"/api/imports/{batch}/rows?pendingOnly=true")).GetProperty("items")[0];
        var candidate = row.GetProperty("candidates")[0].GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/imports/{batch}/rows/{row.GetProperty("id").GetGuid()}/decision", new { decision = "Update", targetStudentId = candidate });
        var result = await (await client.PostAsync(new Uri($"/api/imports/{batch}/confirm", UriKind.Relative), null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{candidate}");
        Assert.Equal(2, student.GetProperty("enrollments").GetArrayLength()); // mudou de turma, histórico preservado
    }

    [Fact]
    public async Task Celula_vazia_nunca_apaga_dado_existente()
    {
        using var client = await DirectorAsync();
        await ImportCsvAsync(client, "CGM;Nome do Aluno;Turma;E-mail Institucional\n5550002002;OTÁVIO FICTÍCIO;7º A Manhã;otavio@aluno.exemplo.edu.br\n");

        var batch = await UploadCsvAsync(client, "CGM;Nome do Aluno;Turma;E-mail Institucional\n5550002002;OTÁVIO FICTÍCIO;7º A Manhã;\n");
        var preview = await MapAsync(client, batch, "CSV", 1, Map(("Code", 0), ("Name", 1), ("Classroom", 2), ("Email", 3)));

        Assert.Equal(1, preview.GetProperty("unchanged").GetInt32());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/students?query=5550002002");
        var student = await client.GetFromJsonAsync<JsonElement>($"/api/students/{list.GetProperty("items")[0].GetProperty("id").GetGuid()}");
        Assert.Equal("otavio@aluno.exemplo.edu.br", student.GetProperty("institutionalEmail").GetString());
    }

    [Fact]
    public async Task Cgm_de_outro_aluno_com_nome_diferente_e_conflito()
    {
        using var client = await DirectorAsync();
        await ImportCsvAsync(client, "CGM;Nome do Aluno;Turma\n5550003003;HELENA FICTÍCIA RAMOS;8º A Manhã\n");

        var batch = await UploadCsvAsync(client, "CGM;Nome do Aluno;Turma\n5550003003;CAIO OUTRO NOME;8º A Manhã\n");
        var preview = await MapAsync(client, batch, "CSV", 1, Map(("Code", 0), ("Name", 1), ("Classroom", 2)));

        Assert.Equal(1, preview.GetProperty("conflict").GetInt32());
    }

    [Fact]
    public async Task Linhas_invalidas_sao_rejeitadas_sem_expor_o_valor()
    {
        using var client = await DirectorAsync();
        var batch = await UploadCsvAsync(client, "CGM;Nome do Aluno;Turma\nABC-SEGREDO;NOME VÁLIDO;6º A Manhã\n5550004004;;6º A Manhã\n5550004005;OUTRO NOME;TURMA-INEXISTENTE-XYZ\n");
        var preview = await MapAsync(client, batch, "CSV", 1, Map(("Code", 0), ("Name", 1), ("Classroom", 2)));

        Assert.Equal(3, preview.GetProperty("rejected").GetInt32());
        var rows = await client.GetStringAsync(new Uri($"/api/imports/{batch}/rows?action=Rejected", UriKind.Relative));
        var errors = JsonDocument.Parse(rows).RootElement.GetProperty("items").EnumerateArray().SelectMany(r => r.GetProperty("errors").EnumerateArray()).Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("CGM inválido", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Nome do aluno em branco", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Turma em formato não reconhecido", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("SEGREDO", StringComparison.Ordinal) || e.Contains("XYZ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("planilha.xls", "D0CF11E0", "xls antigo")]
    [InlineData("foto.png", "89504E47", "Envie um arquivo")]
    public async Task Formatos_nao_aceitos_sao_recusados(string name, string magicHex, string expected)
    {
        using var client = await DirectorAsync();
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. Convert.FromHexString(magicHex), .. new byte[100]]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", name);

        var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Roles.Director, HttpStatusCode.Created)]
    [InlineData(Roles.TechAdmin, HttpStatusCode.Created)]
    [InlineData(Roles.Pedagogue, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.ReadOnly, HttpStatusCode.Forbidden)]
    [InlineData(Roles.PlatformSupport, HttpStatusCode.Forbidden)]
    public async Task So_direcao_e_admin_tecnico_importam(string role, HttpStatusCode expected)
    {
        using var client = fx.SchoolClient(C, await fx.SignInAsync(C, [role]));

        var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), Csv("Nome do Aluno;Turma\nX Y Z;6º A Manhã\n"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Lote_de_uma_escola_nao_e_visivel_em_outra()
    {
        using var inC = await DirectorAsync();
        var batch = await UploadCsvAsync(inC, "Nome do Aluno;Turma\nX Y Z;6º A Manhã\n");
        using var inA = fx.SchoolClient(SineduFixture.SchoolA, await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]));

        Assert.Equal(HttpStatusCode.NotFound, (await inA.GetAsync(new Uri($"/api/imports/{batch}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Mapeamento_confirmado_vira_padrao_do_mesmo_formato_de_planilha()
    {
        using var client = await DirectorAsync();
        const string header = "Identificador Fictício;Estudante (formato próprio);Sala Fictícia";

        // Cabeçalho que a sugestão automática não reconhece: a escola mapeia à mão e confirma.
        var first = await UploadCsvAsync(client, $"{header}\n5550009001;PEDRO FICTÍCIO MAPEADO;6º A Manhã\n");
        var custom = Map(("Code", 0), ("Name", 1), ("Classroom", 2));
        await MapAsync(client, first, "CSV", 1, custom);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(new Uri($"/api/imports/{first}/confirm", UriKind.Relative), null)).StatusCode);

        // Mesmo formato: já abre com o mapeamento salvo.
        var second = await UploadCsvAsync(client, $"{header}\n5550009002;LUANA FICTÍCIA MAPEADA;6º A Manhã\n");
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{second}");
        Assert.True(details.GetProperty("savedMapping").GetBoolean());
        var mapping = details.GetProperty("mapping");
        Assert.Equal(0, mapping.GetProperty("Code").GetInt32());
        Assert.Equal(1, mapping.GetProperty("Name").GetInt32());
        Assert.Equal(2, mapping.GetProperty("Classroom").GetInt32());

        // Outro formato: sugestão automática de sempre.
        var other = await UploadCsvAsync(client, "Nome do Aluno;Turma\nOUTRO FORMATO FICTÍCIO;6º A Manhã\n");
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"/api/imports/{other}")).GetProperty("savedMapping").GetBoolean());
    }

    // ---------- apoio ----------

    private async Task<HttpClient> DirectorAsync()
    {
        var client = fx.SchoolClient(C, await fx.SignInAsync(C, [Roles.Director]));
        await YearLock.WaitAsync();
        try
        {
            if (!_yearCreated)
            {
                var year = await client.PostAsJsonAsync("/api/school/years", new { year = 2026, startsOn = "2026-02-02", endsOn = "2026-12-18", status = "Active", createStandardTerms = true });
                Assert.Equal(HttpStatusCode.Created, year.StatusCode);
                _yearCreated = true;
            }
        }
        finally
        {
            YearLock.Release();
        }

        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string path, string name)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(path)), "file", name);
        var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadCsvAsync(HttpClient client, string csv)
    {
        var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), Csv(csv));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task ImportCsvAsync(HttpClient client, string csv)
    {
        var batch = await UploadCsvAsync(client, csv);
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{batch}");
        await MapAsync(client, batch, "CSV", 1, details.GetProperty("mapping"));
        var confirm = await client.PostAsync(new Uri($"/api/imports/{batch}/confirm", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    private static async Task<JsonElement> MapAsync(HttpClient client, Guid batch, string sheet, int headerRow, object mapping)
    {
        var response = await client.PutAsJsonAsync($"/api/imports/{batch}/mapping", new { sheet, headerRow, mapping });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, int> Map(params (string Field, int Column)[] fields) => fields.ToDictionary(f => f.Field, f => f.Column);

    private static MultipartFormDataContent Csv(string text)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", "alunos.csv");
        return content;
    }

    private static string SamplePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".doc", "amostras")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(".doc/amostras não encontrado"), ".doc", "amostras", "alunos-2026-anonimizado.xlsx");
    }
}
