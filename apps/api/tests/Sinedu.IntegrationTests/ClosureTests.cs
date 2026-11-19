using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Encerramento de escola (P-13, decisão P11): fecha a escola, só a Direção baixa a exportação dos dados comuns
/// (sem Saúde/Proteção), destruição só após a retenção e com confirmação, crypto-shredding da chave.
/// Usa escolas extras para não afetar os demais testes.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class ClosureTests(SineduFixture fx)
{
    [Fact]
    public async Task Encerra_exporta_e_destroi_depois_da_retencao()
    {
        const string slug = "escola-encerrada";
        await fx.ProvisionExtraAsync(slug, "Escola Encerrada (teste)");
        var sample = await SchoolData.EnsureAsync(fx, slug);
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var (_, support) = await fx.PanelClientAsync(Roles.Support);
        var tenant = await TenantAsync(slug);

        // Só SUPER_ADMIN encerra; motivo obrigatório.
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close", new { reason = "Fim do contrato" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close", new { reason = "Fim do contrato" })).StatusCode);
        fx.ForgetTenant(slug);

        // Na escola: tudo fechado, exceto /api/me e a exportação para a Direção.
        using var director = fx.SchoolClient(slug, await fx.SignInAsync(slug, [Roles.Director]));
        var blocked = await director.GetAsync(new Uri("/api/students", UriKind.Relative));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Contains("tenant_closing", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True((await director.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("closing").GetBoolean());

        using var pedagogue = fx.SchoolClient(slug, await fx.SignInAsync(slug, [Roles.Pedagogue]));
        Assert.Equal(HttpStatusCode.Forbidden, (await pedagogue.GetAsync(new Uri("/api/closing/export", UriKind.Relative))).StatusCode);

        var export = await director.GetAsync(new Uri("/api/closing/export", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);
        using (var zip = new ZipArchive(await export.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                ["LEIA-ME.txt", "alunos.csv", "enderecos.csv", "equipe.csv", "matriculas.csv", "ocorrencias.csv", "responsaveis.csv", "turmas.csv"],
                zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray());
            using var reader = new StreamReader(zip.GetEntry("alunos.csv")!.Open(), Encoding.UTF8);
            Assert.Contains(sample.StudentInXName, await reader.ReadToEndAsync(), StringComparison.Ordinal);
            Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("saude", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("prote", StringComparison.OrdinalIgnoreCase));
        }

        // Destruição: antes do prazo → 409; endereço errado → 400.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/destroy", new { confirmSlug = slug })).StatusCode);
        await using (var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema))
        {
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ClosingStartedAt, DateTimeOffset.UtcNow.AddDays(-91)));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/destroy", new { confirmSlug = "outra-escola" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/destroy", new { confirmSlug = slug })).StatusCode);
        fx.ForgetTenant(slug);

        // Banco, papéis, anexos, segredos e chave não existem mais; a escola responde indisponível.
        await using (var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fx.PlatformConnectionString) { Database = "postgres" }.ConnectionString))
        {
            await connection.OpenAsync();
            await using var db = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @d", connection);
            db.Parameters.AddWithValue("d", tenant.DatabaseName);
            Assert.Equal(0L, await db.ExecuteScalarAsync());
            await using var roles = new NpgsqlCommand("SELECT count(*) FROM pg_roles WHERE rolname IN (@o, @a)", connection);
            roles.Parameters.AddWithValue("o", tenant.OwnerRole);
            roles.Parameters.AddWithValue("a", tenant.AppRole);
            Assert.Equal(0L, await roles.ExecuteScalarAsync());
        }

        Assert.DoesNotContain(tenant.TransitKeyName, fx.Vault.KeyNames);
        Assert.Null(await fx.Vault.ReadAsync(tenant.SecretPath));
        Assert.False(fx.Storage.Buckets.ContainsKey(tenant.BucketName));
        Assert.Equal(TenantStatus.Closed, (await TenantAsync(slug)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await director.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);

        // As demais escolas continuam funcionando.
        using var other = fx.SchoolClient(SineduFixture.SchoolA, await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(new Uri("/api/students", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Encerramento_pode_ser_desfeito_antes_da_destruicao_e_exportacao_so_vale_em_encerramento()
    {
        const string slug = "escola-desistiu";
        await fx.ProvisionExtraAsync(slug, "Escola que Desistiu (teste)");
        var (_, admin) = await fx.PanelClientAsync(Roles.SuperAdmin);
        var tenant = await TenantAsync(slug);

        using var director = fx.SchoolClient(slug, await fx.SignInAsync(slug, [Roles.Director]));
        Assert.Equal(HttpStatusCode.NotFound, (await director.GetAsync(new Uri("/api/closing/export", UriKind.Relative))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close/cancel", new { reason = "Não está encerrando" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close", new { reason = "Pedido da escola" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/close/cancel", new { reason = "Escola renovou" })).StatusCode);

        var after = await TenantAsync(slug);
        Assert.Equal(TenantStatus.Suspended, after.Status);
        Assert.Null(after.ClosingStartedAt);
        Assert.Contains(tenant.TransitKeyName, fx.Vault.KeyNames);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/tenants/{tenant.Id}/destroy", new { confirmSlug = slug })).StatusCode);
    }

    private async Task<Sinedu.Platform.Tenants.Tenant> TenantAsync(string slug)
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        return await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == slug);
    }
}
