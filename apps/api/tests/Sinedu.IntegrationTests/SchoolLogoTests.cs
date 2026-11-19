using System.Net;
using System.Net.Http.Headers;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.SharedKernel.Web;

namespace Sinedu.IntegrationTests;

/// <summary>Logo da escola (F5-05): só Direção/Admin técnico enviam; só PNG/JPG; aparece no cabeçalho da ata.</summary>
[Collection(SineduTestGroup.Name)]
public class SchoolLogoTests(SineduFixture fx)
{
    private const string A = SineduFixture.SchoolA;

    // PNG 1x1 válido (fictício).
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData(Roles.Pedagogue)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.ReadOnly)]
    [InlineData(Roles.PlatformSupport)]
    public async Task Sem_papel_de_configuracao_nao_envia_logo(string role)
    {
        using var client = fx.SchoolClient(A, await fx.SignInAsync(A, [role]));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync(new Uri("/api/school/profile/logo", UriKind.Relative), Form(Png, "logo.png"))).StatusCode);
    }

    [Fact]
    public async Task Logo_valido_vai_para_a_ata_e_arquivo_que_nao_e_imagem_e_recusado()
    {
        using var admin = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.TechAdmin]));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(new Uri("/api/school/profile/logo", UriKind.Relative), Form("%PDF-1.4 falso"u8.ToArray(), "logo.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync(new Uri("/api/school/profile/logo", UriKind.Relative), Form(Png, "logo.png"))).StatusCode);

        var logo = await admin.GetAsync(new Uri("/api/school/profile/logo", UriKind.Relative));
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

        // A ata gerada carrega a imagem no cabeçalho.
        using var director = fx.SchoolClient(A, await fx.SignInAsync(A, [Roles.Director]));
        var (studentId, _) = await SchoolData.NewStudentAsync(fx, A);
        var id = await OccurrenceTests.CreateAsync(director, studentId, register: true);
        var withLogo = await (await director.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative))).Content.ReadAsByteArrayAsync();
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(withLogo), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(new Uri("/api/school/profile/logo", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri("/api/school/profile/logo", UriKind.Relative))).StatusCode);
        var withoutLogo = await (await director.GetAsync(new Uri($"/api/occurrences/{id}/ata", UriKind.Relative))).Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("/Subtype /Image", System.Text.Encoding.Latin1.GetString(withoutLogo), StringComparison.Ordinal);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string name)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", name } };
    }
}
