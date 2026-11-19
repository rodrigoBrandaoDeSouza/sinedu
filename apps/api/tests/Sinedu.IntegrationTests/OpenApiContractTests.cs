using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sinedu.IntegrationTests.Infrastructure;

namespace Sinedu.IntegrationTests;

/// <summary>
/// Contrato OpenAPI (F2-11) versionado em .doc/api. Falha quando a API muda e o contrato não foi atualizado.
/// Para regravar: <c>SINEDU_UPDATE_CONTRACT=1 dotnet test --filter OpenApiContractTests</c> e depois <c>npm run api:types</c>.
/// </summary>
[Collection(SineduTestGroup.Name)]
public class OpenApiContractTests(SineduFixture fx)
{
    [Fact]
    public async Task Contrato_da_api_da_escola_esta_atualizado()
    {
        using var client = fx.SchoolClient(SineduFixture.SchoolA);
        await CheckAsync(client, "openapi-escola.json", ["/api/students", "/api/occurrences", "/api/access/support", "/api/closing/export"]);
    }

    [Fact]
    public async Task Contrato_da_api_da_plataforma_esta_atualizado()
    {
        using var client = fx.PlatformClient("painel.localhost");
        await CheckAsync(client, "openapi-plataforma.json", ["/api/tenants", "/api/billing/plans", "/api/support/requests", "/api/networks"]);
    }

    private static async Task CheckAsync(HttpClient client, string file, string[] mustHave)
    {
        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        document.Remove("servers"); // depende do host do teste
        var paths = document["paths"]!.AsObject();
        Assert.All(mustHave, p => Assert.True(paths.ContainsKey(p), $"Contrato sem {p}"));

        var current = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(RepoRoot(), ".doc", "api", file);
        if (Environment.GetEnvironmentVariable("SINEDU_UPDATE_CONTRACT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, current);
            return;
        }

        Assert.True(File.Exists(path), $"Contrato ausente: {path}. Gere com SINEDU_UPDATE_CONTRACT=1.");
        var saved = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.True(saved == current, $"O contrato {file} está desatualizado. Rode com SINEDU_UPDATE_CONTRACT=1 e depois npm run api:types.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
