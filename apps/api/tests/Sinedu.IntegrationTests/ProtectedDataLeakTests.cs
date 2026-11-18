using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Critérios 8–15 da spec (bloqueiam a release). Strings-sentinela são gravadas em Saúde, Proteção, anexo e
/// justificativa de exportação; depois procuramos essas strings em tudo o que outros papéis e a plataforma enxergam:
/// respostas e cabeçalhos HTTP, tabelas brutas (escola e plataforma), objetos no S3, logs e e-mails.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class ProtectedDataLeakTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    /// <summary>Endpoints comuns que um aluno pode atravessar (listas, detalhe, buscas, contagens, turmas, importações).</summary>
    private static readonly string[] CommonEndpoints =
    [
        "/api/me",
        "/api/students",
        "/api/students?search=FICT",
        "/api/students/{id}",
        "/api/students/classroom-counts",
        "/api/school/classrooms",
        "/api/school/classrooms/{classroom}",
        "/api/school/profile",
        "/api/imports",
        "/api/access/members",
    ];

    [Fact]
    public async Task Conteudo_protegido_nao_aparece_para_ninguem_alem_de_direcao_e_pedagogia()
    {
        var (studentId, sentinels) = await SeedSentinelsAsync();
        var sample = await SchoolData.EnsureAsync(fx, A);

        foreach (var role in new[] { Roles.Teacher, Roles.ReadOnly, Roles.TechAdmin, Roles.PlatformSupport, Roles.Director, Roles.Pedagogue })
        {
            var session = await fx.SignInAsync(A, [role]);
            if (role == Roles.Teacher)
            {
                await SchoolData.AssignTeacherAsync(fx, A, session.UserId, sample.ClassroomX);
            }
            else if (role == Roles.ReadOnly)
            {
                await SchoolData.GrantReadOnlyAsync(fx, A, session.UserId, sample.ClassroomX);
            }

            using var client = fx.SchoolClient(A, session);
            foreach (var path in CommonEndpoints)
            {
                var url = path.Replace("{id}", studentId.ToString(), StringComparison.Ordinal).Replace("{classroom}", sample.ClassroomX.ToString(), StringComparison.Ordinal);
                var response = await client.GetAsync(new Uri(url, UriKind.Relative));
                var text = await response.Content.ReadAsStringAsync() + string.Join('\n', response.Headers.Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));
                AssertClean(text, sentinels, $"{role} em {path}");
            }

            // Mesmo o diretor não recebe nada sensível pelos DTOs comuns: nem propriedades nulas.
            var detail = await client.GetAsync(new Uri($"/api/students/{studentId}", UriKind.Relative));
            if (detail.IsSuccessStatusCode)
            {
                var json = await detail.Content.ReadAsStringAsync();
                foreach (var forbidden in new[] { "health", "allerg", "protection", "medication", "bloodType", "sigilo" })
                {
                    Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public async Task Diretor_de_outra_escola_nao_obtem_nada_nem_trocando_ids()
    {
        var (studentId, sentinels) = await SeedSentinelsAsync();
        var other = await fx.SignInAsync(SineduFixture.SchoolB, [Roles.Director]);
        using var client = fx.SchoolClient(SineduFixture.SchoolB, other);

        foreach (var path in new[] { "health", "health/versions", "health/versions/1", "protection", "protected-attachments" })
        {
            var response = await client.GetAsync(new Uri($"/api/students/{studentId}/{path}", UriKind.Relative));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            AssertClean(await response.Content.ReadAsStringAsync(), sentinels, $"escola B em {path}");
        }

        var pending = await client.GetStringAsync(new Uri("/api/protected/pending", UriKind.Relative));
        Assert.DoesNotContain(studentId.ToString(), pending, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bancos_armazenamento_logs_e_emails_nao_tem_conteudo_em_claro()
    {
        var (studentId, sentinels) = await SeedSentinelsAsync();

        // Controles positivos: a varredura enxerga dados comuns, há logs e há objetos (senão o teste estaria cego).
        using (var director = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director])))
        {
            var code = (await director.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}")).GetProperty("institutionalCode").GetString()!;
            Assert.NotEmpty(await DbScan.FindAsync(await fx.SchoolConnectionAsync(A, owner: true), code));
        }

        Assert.NotEmpty(fx.Logs.Lines);
        Assert.NotEmpty(fx.Storage.Objects);

        foreach (var sentinel in sentinels)
        {
            // Tabelas brutas da escola (como num dump/backup lógico) e da plataforma (métricas, auditoria, suporte).
            Assert.Empty(await DbScan.FindAsync(await fx.SchoolConnectionAsync(A, owner: true), sentinel));
            Assert.Empty(await DbScan.FindAsync(fx.PlatformConnectionString, sentinel));

            // Objetos no S3 só têm ciphertext.
            Assert.DoesNotContain(fx.Storage.Objects, o => Encoding.Latin1.GetString(o.Value).Contains(sentinel, StringComparison.Ordinal));

            Assert.DoesNotContain(fx.Logs.Lines, l => l.Contains(sentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(fx.Email.Sent, m => (m.Subject + m.TextBody).Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Ciphertext_copiado_para_outro_aluno_nao_decifra()
    {
        var (victim, sentinels) = await SeedSentinelsAsync();
        var (target, _) = await SchoolData.NewStudentAsync(fx, A);
        using var director = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        await director.PutAsJsonAsync($"/api/students/{target}/health", ProtectedRecordsTests.HealthBody("Nada", "Ficha alvo", null));

        // Alguém com acesso ao banco copia a versão cifrada de um aluno para outro: o AAD impede a leitura.
        await using (var conn = new Npgsql.NpgsqlConnection(await fx.SchoolConnectionAsync(A, owner: true)))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                """
                UPDATE protected.record_versions t SET ciphertext = s.ciphertext, nonce = s.nonce, tag = s.tag, wrapped_key = s.wrapped_key
                FROM protected.record_versions s
                WHERE s.student_id = @victim AND s.record_kind = 'Health' AND t.student_id = @target AND t.record_kind = 'Health'
                """, conn);
            cmd.Parameters.AddWithValue("victim", victim);
            cmd.Parameters.AddWithValue("target", target);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        var response = await director.GetAsync(new Uri($"/api/students/{target}/health", UriKind.Relative));
        Assert.False(response.IsSuccessStatusCode);
        AssertClean(await response.Content.ReadAsStringAsync(), sentinels, "ciphertext trocado");
    }

    private async Task<(Guid StudentId, string[] Sentinels)> SeedSentinelsAsync()
    {
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var id = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        string[] sentinels = [$"SENTINELA-SAUDE-{id}", $"SENTINELA-PROTECAO-{id}", $"SENTINELA-ANEXO-{id}", $"SENTINELA-EXPORT-{id}", $"SENTINELA-MOTIVO-{id}"];
        using var director = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));

        var health = await director.PutAsJsonAsync($"/api/students/{studentId}/health", new
        {
            data = new { allergies = sentinels[0], conditions = sentinels[0], emergencyContacts = new[] { new { name = sentinels[0], phone = "(00) 90000-0003" } } },
            reviewDueOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            reason = sentinels[4],
            expectedVersion = (int?)null,
        });
        health.EnsureSuccessStatusCode();
        var healthId = (await health.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await director.PostAsJsonAsync($"/api/students/{studentId}/protection", new
        {
            kind = "MedidaProtetiva",
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3),
            data = new
            {
                title = sentinels[1], description = sentinels[1], verification = "Documentado",
                unauthorizedPeople = new[] { new { name = sentinels[1], document = "000.000.000-00" } },
            },
            reason = sentinels[4],
        })).EnsureSuccessStatusCode();

        (await director.PostAsync(new Uri($"/api/students/{studentId}/protected-attachments", UriKind.Relative),
            ProtectedRecordsTests.Form("Health", healthId, ProtectedRecordsTests.PdfBytes(sentinels[2]), $"{sentinels[2]}.pdf"))).EnsureSuccessStatusCode();

        (await director.PostAsJsonAsync($"/api/students/{studentId}/protected-export",
            new { health = true, protection = true, justification = $"Ofício do Conselho Tutelar {sentinels[3]}" })).EnsureSuccessStatusCode();

        // Leituras para gerar eventos de auditoria e logs com o conteúdo em memória.
        await director.GetStringAsync(new Uri($"/api/students/{studentId}/health", UriKind.Relative));
        await director.GetStringAsync(new Uri($"/api/students/{studentId}/protection", UriKind.Relative));
        await director.GetStringAsync(new Uri("/api/protected/pending", UriKind.Relative));
        return (studentId, sentinels);
    }

    private static void AssertClean(string text, string[] sentinels, string where)
    {
        foreach (var s in sentinels)
        {
            Assert.False(text.Contains(s, StringComparison.Ordinal), $"Conteúdo protegido vazou: {where}");
        }
    }
}
