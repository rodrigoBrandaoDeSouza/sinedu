using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Saúde e Proteção/Sigilo (Fase 4): matriz de permissões, step-up, isolamento entre escolas, versionamento,
/// anexos cifrados, exportação excepcional, pendências, importação dedicada e rotação de chave.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class ProtectedRecordsTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    public static TheoryData<string, string?, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string?, HttpStatusCode>();
        string[] paths =
        [
            "/api/students/{id}/health",
            "/api/students/{id}/health/versions",
            "/api/students/{id}/protection",
            "/api/students/{id}/protected-attachments",
            "/api/protected/pending",
            "/api/protected/health-import/columns",
        ];
        foreach (var path in paths)
        {
            data.Add(path, null, HttpStatusCode.Unauthorized);
            data.Add(path, Roles.TechAdmin, HttpStatusCode.Forbidden);
            data.Add(path, Roles.Teacher, HttpStatusCode.Forbidden);
            data.Add(path, Roles.ReadOnly, HttpStatusCode.Forbidden);
            data.Add(path, Roles.PlatformSupport, HttpStatusCode.Forbidden);
            data.Add(path, Roles.Pedagogue, HttpStatusCode.OK);
        }

        // Ficha ainda inexistente: diretor recebe 200 com record nulo; histórico de ficha inexistente é 404.
        data.Add("/api/students/{id}/health", Roles.Director, HttpStatusCode.OK);
        data.Add("/api/students/{id}/protection", Roles.Director, HttpStatusCode.OK);
        data.Add("/api/protected/pending", Roles.Director, HttpStatusCode.OK);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_protegido_respeita_a_matriz(string path, string? role, HttpStatusCode expected)
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var session = role is null ? null : await fx.SignInAsync(A, [role]);
        using var client = fx.SchoolClient(A, session);

        var response = await client.GetAsync(new Uri(path.Replace("{id}", studentId.ToString(), StringComparison.Ordinal), UriKind.Relative));

        if (expected == HttpStatusCode.OK && path.EndsWith("/versions", StringComparison.Ordinal))
        {
            expected = HttpStatusCode.NotFound;
        }

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.ReadOnly)]
    [InlineData(Roles.TechAdmin)]
    [InlineData(Roles.PlatformSupport)]
    public async Task Papeis_sem_acesso_nao_escrevem_nem_baixam(string role)
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var health = await CreateHealthAsync(studentId, "Alergia a amendoim");
        var attachment = await UploadAsync(studentId, "Health", health, PdfBytes("laudo"));

        var session = await fx.SignInAsync(A, [role]);
        await GrantScopeAsync(role, session.UserId);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody("x", "Tentativa", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/students/{studentId}/protection", ProtectionBody("x", "Tentativa"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/api/students/{studentId}/protected-attachments/{attachment}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/students/{studentId}/protected-export", new { health = true, protection = true, justification = "Pedido formal do Conselho Tutelar nº 123" })).StatusCode);
    }

    [Fact]
    public async Task Professor_da_turma_do_aluno_continua_sem_acesso()
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        var session = await fx.SignInAsync(A, [Roles.Teacher]);
        await SchoolData.AssignTeacherAsync(fx, A, session.UserId, sample.ClassroomX);
        using var client = fx.SchoolClient(A, session);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri($"/api/students/{sample.StudentInX}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/api/students/{sample.StudentInX}/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Negativa_e_auditada_sem_conteudo()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var session = await fx.SignInAsync(A, [Roles.Teacher]);
        using var client = fx.SchoolClient(A, session);

        await client.GetAsync(new Uri($"/api/students/{studentId}/health", UriKind.Relative));

        await using var audit = await AuditAsync();
        Assert.True(await audit.Events.AnyAsync(e => e.Action == "protected_access_denied" && e.StudentId == studentId && e.UserId == session.UserId));
    }

    [Fact]
    public async Task Step_up_vencido_ou_sem_mfa_pede_reautenticacao()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var old = await fx.SignInAsync(A, [Roles.Director], authAge: TimeSpan.FromMinutes(16));
        var noMfa = await fx.SignInAsync(A, [Roles.Pedagogue], mfa: false);

        foreach (var session in new[] { old, noMfa })
        {
            using var client = fx.SchoolClient(A, session);
            var response = await client.GetAsync(new Uri($"/api/students/{studentId}/health", UriKind.Relative));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("step_up_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(Roles.Director)]
    [InlineData(Roles.Pedagogue)]
    public async Task Direcao_e_pedagogia_de_outra_escola_recebem_404(string role)
    {
        var (studentInA, _) = await SchoolData.NewStudentAsync(fx, A);
        await CreateHealthAsync(studentInA, "Asma leve");

        var other = await fx.SignInAsync(SineduFixture.SchoolB, [role]);
        using var client = fx.SchoolClient(SineduFixture.SchoolB, other);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/students/{studentInA}/health", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/api/students/{studentInA}/protection", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/students/{studentInA}/health", HealthBody("x", "Tentativa de outra escola", null))).StatusCode);
    }

    [Fact]
    public async Task Sessao_de_uma_escola_nao_vale_em_outra()
    {
        var (studentInA, _) = await SchoolData.NewStudentAsync(fx, A);
        var director = await fx.SignInAsync(A, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolB, director);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri($"/api/students/{studentInA}/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Ficha_de_saude_e_versionada_e_nao_sobrescreve_em_silencio()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        using var pedagogue = await ClientAsync(Roles.Pedagogue);

        var first = await director.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody("Alergia a dipirona", "Informado pela mãe na matrícula", null));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await pedagogue.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody("Alergia a dipirona e a látex", "Atestado entregue em novembro", 1));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Quem estava com a versão 1 aberta não sobrescreve a 2.
        var stale = await director.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody("Outro texto", "Edição concorrente", 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var noReason = await director.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody("Outro texto", "", 2));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var current = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/health");
        Assert.Equal(2, current.GetProperty("record").GetProperty("version").GetInt32());
        Assert.Equal("Alergia a dipirona e a látex", current.GetProperty("record").GetProperty("data").GetProperty("allergies").GetString());

        var versions = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/health/versions");
        Assert.Equal(2, versions.GetArrayLength());

        var v1 = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/health/versions/1");
        Assert.Equal("Alergia a dipirona", v1.GetProperty("data").GetProperty("allergies").GetString());
        Assert.Equal("Informado pela mãe na matrícula", v1.GetProperty("reason").GetString());

        var response = await director.GetAsync(new Uri($"/api/students/{studentId}/health", UriKind.Relative));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);

        await using var audit = await AuditAsync();
        var actions = await audit.Events.Where(e => e.StudentId == studentId).Select(e => e.Action).ToListAsync();
        Assert.Contains("health_updated", actions);
        Assert.Contains("health_viewed", actions);
        Assert.Contains("health_version_viewed", actions);
    }

    [Fact]
    public async Task Protecao_tem_varios_registros_vigencia_e_revogacao_com_historico()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);

        var created = await director.PostAsJsonAsync($"/api/students/{studentId}/protection", ProtectionBody("Guarda unilateral da mãe", "Termo judicial apresentado"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = await created.Content.ReadFromJsonAsync<JsonElement>();
        var recordId = record.GetProperty("id").GetGuid();
        Assert.Equal("Vigente", record.GetProperty("status").GetString());

        var second = await director.PostAsJsonAsync($"/api/students/{studentId}/protection", ProtectionBody("Impedimento de retirada pelo pai", "Medida protetiva vigente", kind: "MedidaProtetiva"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var updated = await director.PutAsJsonAsync($"/api/students/{studentId}/protection/{recordId}",
            ProtectionBody("Guarda unilateral da mãe (atualizada)", "Nova decisão juntada", expectedVersion: 1));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var revoked = await director.PostAsJsonAsync($"/api/students/{studentId}/protection/{recordId}/revoke", new { reason = "Decisão judicial revogada em audiência", expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal("Revogada", (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var afterRevoke = await director.PutAsJsonAsync($"/api/students/{studentId}/protection/{recordId}",
            ProtectionBody("Tentativa", "Editar revogado", expectedVersion: 3));
        Assert.Equal(HttpStatusCode.BadRequest, afterRevoke.StatusCode);

        var list = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/protection");
        Assert.Equal(2, list.GetArrayLength());

        var history = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/protection/{recordId}/versions");
        Assert.Equal(3, history.GetArrayLength());
        var v1 = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/protection/{recordId}/versions/1");
        Assert.Equal("Guarda unilateral da mãe", v1.GetProperty("data").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Registro_de_outro_aluno_nao_e_acessivel_trocando_ids()
    {
        var (owner, _) = await SchoolData.NewStudentAsync(fx, A);
        var (other, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var created = await (await director.PostAsJsonAsync($"/api/students/{owner}/protection", ProtectionBody("Tutela", "Registro de teste"))).Content.ReadFromJsonAsync<JsonElement>();
        var recordId = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await director.GetAsync(new Uri($"/api/students/{other}/protection/{recordId}/versions", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await director.GetAsync(new Uri($"/api/students/{Guid.NewGuid()}/protection", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Anexo_e_cifrado_no_armazenamento_e_baixado_so_pela_api()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var healthId = await CreateHealthAsync(studentId, "Diabetes tipo 1");
        var marker = "LAUDO-" + Guid.NewGuid().ToString("N");
        var content = PdfBytes(marker);

        var attachmentId = await UploadAsync(studentId, "Health", healthId, content, "laudo medico.pdf");

        Assert.DoesNotContain(fx.Storage.Objects, o => Encoding.Latin1.GetString(o.Value).Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(fx.Storage.Objects.Keys, k => k.Contains("laudo", StringComparison.OrdinalIgnoreCase));

        using var director = await ClientAsync(Roles.Director);
        var list = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/protected-attachments");
        Assert.Equal("laudo medico.pdf", list[0].GetProperty("fileName").GetString());

        var download = await director.GetAsync(new Uri($"/api/students/{studentId}/protected-attachments/{attachmentId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", download.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MZ executável", "programa.pdf")]
    [InlineData("texto qualquer", "nota.txt")]
    public async Task Anexo_recusa_formatos_nao_permitidos(string body, string name)
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var healthId = await CreateHealthAsync(studentId, "Sem alergias conhecidas");
        using var director = await ClientAsync(Roles.Director);

        var response = await director.PostAsync(new Uri($"/api/students/{studentId}/protected-attachments", UriKind.Relative), Form("Health", healthId, Encoding.UTF8.GetBytes(body), name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exportacao_exige_justificativa_e_fica_auditada()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        await CreateHealthAsync(studentId, "Epilepsia controlada");
        using var director = await ClientAsync(Roles.Director);

        var missing = await director.PostAsJsonAsync($"/api/students/{studentId}/protected-export", new { health = true, protection = false, justification = "curta" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var ok = await director.PostAsJsonAsync($"/api/students/{studentId}/protected-export",
            new { health = true, protection = true, justification = "Requisição do Conselho Tutelar, ofício nº 45/2026" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
        var pdf = await ok.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);

        await using var audit = await AuditAsync();
        Assert.True(await audit.Events.AnyAsync(e => e.Action == "protected_exported" && e.StudentId == studentId));
    }

    [Fact]
    public async Task Pendencias_listam_revisoes_e_medidas_vencidas_sem_conteudo()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await director.PutAsJsonAsync($"/api/students/{studentId}/health",
            new { data = new { allergies = "Alergia a camarão" }, reviewDueOn = today.AddDays(10), reason = "Cadastro inicial", expectedVersion = (int?)null });
        await director.PostAsJsonAsync($"/api/students/{studentId}/protection",
            new { kind = "MedidaProtetiva", validUntil = today.AddDays(-1), data = new { title = "Medida de afastamento", verification = "Documentado" }, reason = "Ofício recebido" });

        var response = await director.GetAsync(new Uri("/api/protected/pending", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(body).RootElement.EnumerateArray().Where(i => i.GetProperty("studentId").GetGuid() == studentId).ToList();

        Assert.Contains(items, i => i.GetProperty("type").GetString() == "health_review");
        Assert.Contains(items, i => i.GetProperty("type").GetString() == "protection_expired");
        Assert.DoesNotContain("camarão", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("afastamento", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importacao_de_saude_usa_so_cgm_previa_sem_valores_e_nao_guarda_arquivo()
    {
        var (s1, code1) = await SchoolData.NewStudentAsync(fx, A);
        var (s2, code2) = await SchoolData.NewStudentAsync(fx, A);
        await CreateHealthAsync(s2, "Alergia a pólen");
        var marker = "IMPORT-" + Guid.NewGuid().ToString("N")[..10];
        var csv = string.Join("\n",
            "CGM;Nome;Alergias;Medicamentos;Contato de emergência;Telefone de emergência",
            $"{code1};CAIO;Alergia a {marker};;Avó Fictícia;(00) 90000-0001",
            $"{code2};OUTRO;;Insulina {marker};;",
            $"{code1};REPETIDO;Outra {marker};;;",
            $"9999999999;INEXISTENTE;Alergia {marker};;;",
            $";SEM CGM;Alergia {marker};;;");
        using var director = await ClientAsync(Roles.Director);

        var preview = await director.PostAsync(new Uri("/api/protected/health-import", UriKind.Relative), CsvForm(csv, confirm: false, reason: null));
        var previewBody = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.DoesNotContain(marker, previewBody, StringComparison.Ordinal);
        var p = JsonDocument.Parse(previewBody).RootElement;
        Assert.Equal(1, p.GetProperty("new").GetInt32());
        Assert.Equal(1, p.GetProperty("update").GetInt32());
        Assert.Equal(1, p.GetProperty("duplicate").GetInt32());
        Assert.Equal(1, p.GetProperty("notFound").GetInt32());
        Assert.Equal(1, p.GetProperty("noCode").GetInt32());
        Assert.Equal(0, p.GetProperty("applied").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await director.GetFromJsonAsync<JsonElement>($"/api/students/{s1}/health")).GetProperty("record").ValueKind);

        var noReason = await director.PostAsync(new Uri("/api/protected/health-import", UriKind.Relative), CsvForm(csv, confirm: true, reason: null));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var confirm = await director.PostAsync(new Uri("/api/protected/health-import", UriKind.Relative), CsvForm(csv, confirm: true, reason: "Fichas de saúde entregues pelas famílias"));
        Assert.Equal(2, (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").GetInt32());

        var h1 = await director.GetFromJsonAsync<JsonElement>($"/api/students/{s1}/health");
        Assert.Equal($"Alergia a {marker}", h1.GetProperty("record").GetProperty("data").GetProperty("allergies").GetString());
        var h2 = await director.GetFromJsonAsync<JsonElement>($"/api/students/{s2}/health");
        Assert.Equal("Alergia a pólen", h2.GetProperty("record").GetProperty("data").GetProperty("allergies").GetString());
        Assert.Equal($"Insulina {marker}", h2.GetProperty("record").GetProperty("data").GetProperty("medications").GetString());

        // A importação comum não aceita o fluxo e o arquivo não ficou em lugar nenhum.
        Assert.DoesNotContain(fx.Storage.Objects, o => Encoding.UTF8.GetString(o.Value).Contains(marker, StringComparison.Ordinal));
        Assert.Empty(await DbScan.FindAsync(await fx.SchoolConnectionAsync(A, owner: true), marker));
    }

    [Fact]
    public async Task Rotacao_de_chave_recifra_as_deks_sem_perder_o_conteudo()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        await CreateHealthAsync(studentId, "Intolerância à lactose");
        var before = fx.Vault.Rewraps;

        await using (var scope = fx.School.Services.CreateAsyncScope())
        {
            var resolution = await scope.ServiceProvider.GetRequiredService<Sinedu.SharedKernel.Tenancy.ITenantResolver>().ResolveAsync(A);
            scope.ServiceProvider.GetRequiredService<Sinedu.SharedKernel.Tenancy.TenantScope>().Set(resolution.Tenant!);
            var result = await scope.ServiceProvider.GetRequiredService<Sinedu.Tenant.ProtectedRecords.KeyRewrap>().RunAsync(CancellationToken.None);
            Assert.True(result.Versions >= 1);
        }

        Assert.True(fx.Vault.Rewraps > before);
        using var director = await ClientAsync(Roles.Director);
        var health = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/health");
        Assert.Equal("Intolerância à lactose", health.GetProperty("record").GetProperty("data").GetProperty("allergies").GetString());
    }

    // ---------- apoio ----------

    private async Task<HttpClient> ClientAsync(string role) => fx.SchoolClient(A, await fx.SignInAsync(A, [role]));

    private async Task<Guid> CreateHealthAsync(Guid studentId, string allergies)
    {
        using var director = await ClientAsync(Roles.Director);
        var current = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}/health");
        int? version = current.GetProperty("record").ValueKind == JsonValueKind.Null ? null : current.GetProperty("record").GetProperty("version").GetInt32();
        var response = await director.PutAsJsonAsync($"/api/students/{studentId}/health", HealthBody(allergies, "Cadastro de teste", version));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadAsync(Guid studentId, string owner, Guid ownerId, byte[] content, string name = "documento.pdf")
    {
        using var director = await ClientAsync(Roles.Director);
        var response = await director.PostAsync(new Uri($"/api/students/{studentId}/protected-attachments", UriKind.Relative), Form(owner, ownerId, content, name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task GrantScopeAsync(string role, Guid userId)
    {
        var sample = await SchoolData.EnsureAsync(fx, A);
        if (role == Roles.Teacher)
        {
            await SchoolData.AssignTeacherAsync(fx, A, userId, sample.ClassroomX);
        }
        else if (role == Roles.ReadOnly)
        {
            await SchoolData.GrantReadOnlyAsync(fx, A, userId, sample.ClassroomX);
        }
    }

    private async Task<AuditDbContext> AuditAsync() =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(A, owner: true), AuditDbContext.Schema).Options);

    internal static object HealthBody(string allergies, string reason, int? expectedVersion) =>
        new { data = new { allergies, bloodType = "O+", emergencyContacts = new[] { new { name = "Tia Fictícia", phone = "(00) 90000-0002" } } }, reason, expectedVersion };

    internal static object ProtectionBody(string title, string reason, string kind = "Guarda", int? expectedVersion = null) =>
        new { kind, validUntil = (DateOnly?)null, data = new { title, description = "Descrição de teste", verification = "Documentado" }, reason, expectedVersion };

    internal static byte[] PdfBytes(string marker) => Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n%%EOF\n");

    internal static MultipartFormDataContent Form(string owner, Guid ownerId, byte[] content, string name)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent
        {
            { file, "file", name },
            { new StringContent(owner), "owner" },
            { new StringContent(ownerId.ToString()), "ownerId" },
        };
    }

    private static MultipartFormDataContent CsvForm(string csv, bool confirm, string? reason)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "saude.csv" }, { new StringContent(confirm ? "true" : "false"), "confirm" } };
        if (reason is not null)
        {
            form.Add(new StringContent(reason), "reason");
        }

        return form;
    }
}
