using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Notifications;

namespace Sinedu.IntegrationTests;

/// <summary>Comunicação com responsáveis (spec §6; critérios 5 e 15).</summary>
[Collection(SineduTestGroup.Name)]
public class CommunicationTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;
    private static readonly Guid Invite = new("0193a000-0000-7000-8000-000000000201");

    [Fact]
    public async Task Sem_autorizacao_de_contato_o_responsavel_nao_recebe()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = await ClientAsync(Roles.Director);
        var occurrence = await OccurrenceTests.CreateAsync(director, studentId, register: true);

        var preview = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{occurrence}/communications/preview");
        var guardian = preview.GetProperty("recipients")[0];
        Assert.False(guardian.GetProperty("eligible").GetBoolean());
        Assert.Equal("Sem autorização de contato registrada.", guardian.GetProperty("reason").GetString());
        Assert.Contains(preview.GetProperty("channels").EnumerateArray(), c => c.GetProperty("channel").GetString() == "WhatsApp" && !c.GetProperty("enabled").GetBoolean());

        var send = await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications",
            new { templateId = Invite, guardianIds = new[] { guardian.GetProperty("guardianId").GetGuid() }, requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);

        // Autorizar exige fundamento.
        var noBasis = await director.PutAsJsonAsync($"/api/students/{studentId}/guardians/{guardian.GetProperty("guardianId").GetGuid()}/contact",
            new { email = "familia@exemplo.test", contactAuthorized = true, isPrimaryContact = true });
        Assert.Equal(HttpStatusCode.BadRequest, noBasis.StatusCode);
    }

    /// <summary>Critério 5: tentativa, sucesso, auditoria; e a mensagem é neutra (critério 15).</summary>
    [Fact]
    public async Task Envio_autorizado_gera_tentativa_sucesso_e_auditoria_com_texto_neutro()
    {
        var (studentId, guardianId, email, occurrence, director) = await ReadyAsync("Aluno arremessou o estojo do colega durante o recreio.");

        var preview = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{occurrence}/communications/preview?templateId={Invite}");
        var body = preview.GetProperty("recipients")[0].GetProperty("body").GetString()!;
        Assert.Contains("gostaria de conversar", body, StringComparison.Ordinal);
        Assert.DoesNotContain("estojo", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Indisciplina", body, StringComparison.OrdinalIgnoreCase);

        var response = await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications",
            new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = (await response.Content.ReadFromJsonAsync<JsonElement>())[0];
        Assert.Equal("Enviado", sent.GetProperty("status").GetString());
        Assert.Equal(1, sent.GetProperty("attempts").GetInt32());

        var mail = Assert.Single(fx.Email.Sent, m => m.To == email);
        Assert.DoesNotContain("estojo", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recreio", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAIO FICTÍCIO", mail.TextBody, StringComparison.Ordinal); // só o primeiro nome do aluno

        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSineduPostgres(await fx.SchoolConnectionAsync(A, owner: true), AuditDbContext.Schema).Options);
        var actions = await audit.Events.Where(e => e.StudentId == studentId).Select(e => e.Action).ToListAsync();
        Assert.Contains("communication_authorized", actions);
        Assert.Contains("communication_sent", actions);
        director.Dispose();
    }

    [Fact]
    public async Task Mesmo_pedido_repetido_nao_duplica_e_segundo_pedido_exige_confirmacao()
    {
        var (_, guardianId, email, occurrence, director) = await ReadyAsync();
        var request = new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() };

        var first = await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications", request);
        var again = await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications", request);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid(), (await again.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid());
        Assert.Single(fx.Email.Sent, m => m.To == email);

        var other = new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Conflict, (await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications", other)).StatusCode);

        var confirmed = new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid(), confirmDuplicate = true };
        Assert.Equal(HttpStatusCode.OK, (await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications", confirmed)).StatusCode);
        Assert.Equal(2, fx.Email.Sent.Count(m => m.To == email));
        director.Dispose();
    }

    [Fact]
    public async Task Falha_fica_registrada_sem_dados_e_pode_ser_reenviada()
    {
        var (_, guardianId, email, occurrence, director) = await ReadyAsync();
        fx.Email.FailuresFor[email] = 1;

        var response = await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications",
            new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() });
        var failed = (await response.Content.ReadFromJsonAsync<JsonElement>())[0];
        Assert.Equal("Falhou", failed.GetProperty("status").GetString());
        var error = failed.GetProperty("lastError").GetString()!;
        Assert.DoesNotContain(email, error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMTP recusou", error, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.String, failed.GetProperty("nextAttemptAt").ValueKind);

        var retry = await director.PostAsync(new Uri($"/api/communications/{failed.GetProperty("id").GetGuid()}/retry", UriKind.Relative), null);
        var ok = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Enviado", ok.GetProperty("status").GetString());
        Assert.Equal(2, ok.GetProperty("history").GetArrayLength());
        Assert.Equal("reenvio manual", ok.GetProperty("history")[1].GetProperty("trigger").GetString());

        // Enviada não é reenviada pelo botão de falha.
        Assert.Equal(HttpStatusCode.Conflict, (await director.PostAsync(new Uri($"/api/communications/{failed.GetProperty("id").GetGuid()}/retry", UriKind.Relative), null)).StatusCode);
        director.Dispose();
    }

    [Fact]
    public async Task Fila_reenvia_automaticamente_quando_chega_a_hora()
    {
        var (_, guardianId, email, occurrence, director) = await ReadyAsync();
        fx.Email.FailuresFor[email] = 1;
        var failed = (await (await director.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications",
            new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() })).Content.ReadFromJsonAsync<JsonElement>())[0];
        var id = failed.GetProperty("id").GetGuid();

        await using (var conn = new NpgsqlConnection(await fx.SchoolConnectionAsync(A, owner: true)))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE notifications.notifications SET next_attempt_at = now() - interval '1 second' WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var scope = fx.School.Services.CreateAsyncScope())
        {
            var resolution = await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync(A);
            scope.ServiceProvider.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
            Assert.True(await scope.ServiceProvider.GetRequiredService<INotificationRetries>().ProcessDueAsync(CancellationToken.None) >= 1);
        }

        var list = await director.GetFromJsonAsync<JsonElement>($"/api/occurrences/{occurrence}/communications");
        var after = list.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == id);
        Assert.Equal("Enviado", after.GetProperty("status").GetString());
        Assert.Equal("reenvio automático", after.GetProperty("history")[1].GetProperty("trigger").GetString());
        director.Dispose();
    }

    [Fact]
    public async Task Rascunho_e_ocorrencia_cancelada_nao_geram_comunicacao()
    {
        var (studentId, guardianId, _, _, director) = await ReadyAsync();
        var draft = await OccurrenceTests.CreateAsync(director, studentId, register: false);
        Assert.Equal(HttpStatusCode.NotFound, (await director.GetAsync(new Uri($"/api/occurrences/{draft}/communications/preview", UriKind.Relative))).StatusCode);

        var cancelled = await OccurrenceTests.CreateAsync(director, studentId, register: true);
        await director.PostAsJsonAsync($"/api/occurrences/{cancelled}/status", new { status = "Cancelada", reason = "Registrada em duplicidade", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, (await director.PostAsJsonAsync($"/api/occurrences/{cancelled}/communications",
            new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() })).StatusCode);
        director.Dispose();
    }

    [Fact]
    public async Task Modelo_so_aceita_marcadores_neutros_e_so_direcao_edita()
    {
        using var director = await ClientAsync(Roles.Director);
        using var pedagogue = await ClientAsync(Roles.Pedagogue);
        var leaky = new { name = "Modelo", subject = "{escola}: aviso", body = "Olá {responsavel}, houve {descricao} com {aluno}. Procure a escola.", isActive = true };

        Assert.Equal(HttpStatusCode.Forbidden, (await pedagogue.PutAsJsonAsync($"/api/communications/templates/{Invite}", leaky)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await director.PutAsJsonAsync($"/api/communications/templates/{Invite}", leaky)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.TechAdmin)]
    [InlineData(Roles.ReadOnly)]
    public async Task Papeis_sem_acesso_nao_preparam_nem_reenviam(string role)
    {
        var (_, guardianId, _, occurrence, director) = await ReadyAsync();
        director.Dispose();
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [role]));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/api/occurrences/{occurrence}/communications/preview", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/occurrences/{occurrence}/communications",
            new { templateId = Invite, guardianIds = new[] { guardianId }, requestId = Guid.NewGuid() })).StatusCode);
    }

    // ---------- apoio ----------

    private async Task<HttpClient> ClientAsync(string role) => fx.SchoolClient(A, await fx.SignInAsync(A, [role]));

    /// <summary>Aluno novo, responsável com e-mail e autorização registrada, e uma ocorrência registrada.</summary>
    private async Task<(Guid StudentId, Guid GuardianId, string Email, Guid OccurrenceId, HttpClient Director)> ReadyAsync(string? description = null)
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var director = await ClientAsync(Roles.Director);
        var student = await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}");
        var guardianId = student.GetProperty("guardians")[0].GetProperty("id").GetGuid();
        var email = $"familia-{Guid.NewGuid():N}@exemplo.test";
        var contact = await director.PutAsJsonAsync($"/api/students/{studentId}/guardians/{guardianId}/contact",
            new { email, contactAuthorized = true, contactBasis = "Termo assinado na matrícula de 2026", isPrimaryContact = true });
        Assert.Equal(HttpStatusCode.NoContent, contact.StatusCode);
        var occurrence = await OccurrenceTests.CreateAsync(director, studentId, register: true, description: description);
        return (studentId, guardianId, email, occurrence, director);
    }
}
