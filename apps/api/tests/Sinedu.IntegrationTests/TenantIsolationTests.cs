using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Critério de bloqueio de release (spec §10.3): nada de uma escola é usável em outra.</summary>
[Collection(SineduTestGroup.Name)]
public class TenantIsolationTests(SineduFixture fx)
{
    [Fact]
    public async Task Sessao_de_uma_escola_nao_vale_em_outra()
    {
        var sessionA = await fx.SignInAsync(SineduFixture.SchoolA, [Roles.Director]);

        using var inA = fx.SchoolClient(SineduFixture.SchoolA, sessionA);
        using var inB = fx.SchoolClient(SineduFixture.SchoolB, sessionA);

        Assert.Equal(HttpStatusCode.OK, (await inA.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await inB.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await inB.GetAsync(new Uri("/api/access/members", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Papel_da_aplicacao_de_uma_escola_nao_conecta_no_banco_de_outra()
    {
        await using var other = await fx.SchoolConnectionAsync(SineduFixture.SchoolA) is var a
            ? new NpgsqlConnection(new NpgsqlConnectionStringBuilder(a) { Database = "sinedu_t_escola_b" }.ConnectionString)
            : null;

        var error = await Assert.ThrowsAsync<PostgresException>(() => other!.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Papel_da_aplicacao_nao_altera_estrutura_nem_apaga_auditoria()
    {
        await using var connection = new NpgsqlConnection(await fx.SchoolConnectionAsync(SineduFixture.SchoolA));
        await connection.OpenAsync();

        await Execute(connection, "INSERT INTO audit.audit_events (occurred_at, action, result, is_support_session) VALUES (now(), 'teste', 'Allowed', false)");

        var delete = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, "DELETE FROM audit.audit_events"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);

        var ddl = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, "CREATE TABLE access.invasora (id int)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ddl.SqlState);
    }

    [Fact]
    public async Task Auditoria_e_somente_inclusao_mesmo_para_o_dono_do_banco()
    {
        await using var connection = new NpgsqlConnection(await fx.SchoolConnectionAsync(SineduFixture.SchoolA, owner: true));
        await connection.OpenAsync();
        await Execute(connection, "INSERT INTO audit.audit_events (occurred_at, action, result, is_support_session) VALUES (now(), 'teste', 'Allowed', false)");

        var error = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, "UPDATE audit.audit_events SET action = 'adulterado'"));
        Assert.Contains("somente inclusão", error.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cada_escola_tem_banco_chave_e_bucket_proprios()
    {
        await using var db = fx.PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        var tenants = await db.Tenants.Where(t => t.Slug == SineduFixture.SchoolA || t.Slug == SineduFixture.SchoolB).ToListAsync();

        Assert.Equal(2, tenants.Select(t => t.DatabaseName).Distinct().Count());
        Assert.Equal(2, tenants.Select(t => t.TransitKeyName).Distinct().Count());
        Assert.All(tenants, t => Assert.Contains(t.TransitKeyName, fx.Vault.KeyNames));
        Assert.All(tenants, t => Assert.True(fx.Storage.Buckets.ContainsKey(t.BucketName)));
    }

    [Fact]
    public async Task Provisionamento_convida_so_o_primeiro_diretor()
    {
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(fx.PlatformConnectionString).UseSnakeCaseNamingConvention().UseOpenIddict().Options);
        var director = await identity.Users.Include(u => u.Memberships).SingleAsync(u => u.Email == "diretor@escola-a.local");

        Assert.Equal(AccountStatus.Invited, director.Status);
        Assert.Single(director.Memberships);
        Assert.True(director.Memberships[0].Privileged);
        Assert.Contains(fx.Email.Sent, m => m.To == "diretor@escola-a.local" && m.TextBody.Contains("convite?token=", StringComparison.Ordinal));
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
