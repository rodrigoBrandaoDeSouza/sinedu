using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Identity;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Painel da plataforma (P-10, P-12, P-14): métricas só agregadas, papel de métricas sem acesso a tabelas,
/// contas da plataforma e nenhum dado de aluno alcançável pelo painel.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class PlatformPanelTests(SineduFixture fx)
{
    [Fact]
    public async Task Coleta_de_metricas_traz_contagens_da_escola()
    {
        await SchoolData.EnsureAsync(fx, SineduFixture.SchoolA);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(new Uri("/api/metrics/collect", UriKind.Relative), null)).StatusCode);
        var metrics = await admin.GetFromJsonAsync<JsonElement>("/api/metrics");
        var row = metrics.GetProperty("tenants").EnumerateArray().Single(t => t.GetProperty("slug").GetString() == SineduFixture.SchoolA);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("error").ValueKind);
        Assert.True(row.GetProperty("activeStudents").GetInt32() >= 2);
        Assert.True(row.GetProperty("activeClassrooms").GetInt32() >= 2);
        Assert.Equal(TenantSchema.Version, row.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task Papel_de_metricas_so_le_a_view_de_contagens()
    {
        var password = (await fx.Vault.ReadAsync(MetricsContract.SecretPath))!["password"];
        var tenant = await fx.SchoolConnectionAsync(SineduFixture.SchoolA);
        var cs = new NpgsqlConnectionStringBuilder(tenant) { Username = MetricsContract.Role, Password = password };
        await using var conn = new NpgsqlConnection(cs.ConnectionString);
        await conn.OpenAsync();

        await using (var view = new NpgsqlCommand("SELECT active_students FROM metrics.v_counts", conn))
        {
            Assert.NotNull(await view.ExecuteScalarAsync());
        }

        foreach (var table in new[] { "students.students", "students.guardians", "protected.record_versions", "protected.health_records", "occurrences.occurrences", "notifications.notifications", "audit.audit_events" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT 1 FROM {table} LIMIT 1", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
            Assert.Equal("42501", ex.SqlState); // permission denied
        }

        // A view não tem coluna de Saúde/Proteção (nem contagem).
        await using var columns = new NpgsqlCommand(
            "SELECT string_agg(column_name, ',') FROM information_schema.columns WHERE table_schema = 'metrics' AND table_name = 'v_counts'", conn);
        var names = (string)(await columns.ExecuteScalarAsync())!;
        Assert.DoesNotMatch("health|protect|sigil|saude|medic|allerg", names);
    }

    [Fact]
    public async Task Suporte_le_metricas_mas_nao_coleta_nem_administra_contas()
    {
        var (_, support) = await fx.PanelClientAsync(Roles.Support);

        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri("/api/metrics", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsync(new Uri("/api/metrics/collect", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync(new Uri("/api/accounts", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"/api/accounts/users/{Guid.NewGuid()}/reset-mfa", new { reason = "Teste de permissão do suporte" })).StatusCode);
    }

    [Fact]
    public async Task Reset_de_mfa_avisa_a_pessoa_derruba_sessoes_e_fica_auditado()
    {
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var (targetId, _) = await fx.PanelClientAsync(Roles.Support);
        var lookup = await admin.GetFromJsonAsync<JsonElement>($"/api/accounts/lookup?email=plataforma-{targetId:N}@sinedu.local");
        Assert.True(lookup.GetProperty("mfaEnabled").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/accounts/users/{targetId}/reset-mfa", new { reason = "curto" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/users/{targetId}/reset-mfa", new { reason = "Perdeu o celular; identidade confirmada por telefone" })).StatusCode);

        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSineduPostgres(fx.PlatformConnectionString, IdentityDbContext.Schema).UseOpenIddict().Options);
        var user = await identity.Users.AsNoTracking().FirstAsync(u => u.Id == targetId);
        Assert.False(user.MfaEnabled);
        Assert.Null(user.TotpSecretEncrypted);
        Assert.Contains(fx.Email.Sent, m => m.To == user.Email && m.Subject.Contains("duas etapas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ninguem_retira_o_proprio_acesso_e_conta_de_escola_nao_vira_conta_da_plataforma()
    {
        var (adminId, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(new Uri($"/api/accounts/{adminId}/revoke", UriKind.Relative), null)).StatusCode);

        // E-mail de uma conta de escola já existente: a plataforma exige outra conta (D19).
        var schoolEmail = $"diretor@{SineduFixture.SchoolA}.local";
        var invite = await admin.PostAsJsonAsync("/api/accounts", new { email = schoolEmail, fullName = "Diretor", role = Roles.Support });
        Assert.Equal(HttpStatusCode.Conflict, invite.StatusCode);
    }

    /// <summary>P-14: nada do que o painel responde contém dados de aluno — nem chamando a API da escola com a sessão do painel.</summary>
    [Fact]
    public async Task Nenhum_dado_de_aluno_e_alcancavel_pelo_painel()
    {
        var sample = await SchoolData.EnsureAsync(fx, SineduFixture.SchoolA);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        await admin.PostAsync(new Uri("/api/metrics/collect", UriKind.Relative), null);

        foreach (var path in new[] { "/api/metrics", "/api/tenants", "/api/networks", "/api/accounts", "/api/me" })
        {
            var body = await (await admin.GetAsync(new Uri(path, UriKind.Relative))).Content.ReadAsStringAsync();
            Assert.DoesNotContain(sample.StudentInXName, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sample.StudentInX.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        // O cookie do painel não abre a API da escola.
        var cookie = admin.DefaultRequestHeaders.GetValues("Cookie").First();
        using var school = fx.SchoolClient(SineduFixture.SchoolA);
        school.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await school.GetAsync(new Uri($"/api/students/{sample.StudentInX}", UriKind.Relative))).StatusCode);
    }
}
