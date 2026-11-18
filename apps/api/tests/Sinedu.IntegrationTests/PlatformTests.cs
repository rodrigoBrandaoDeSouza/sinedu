using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Sinedu.IntegrationTests.Infrastructure;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Identity.Security;
using Sinedu.Platform.Tenants;

namespace Sinedu.IntegrationTests;

[Collection(SineduTestGroup.Name)]
public class PlatformTests(SineduFixture fx)
{
    [Fact]
    public async Task Api_do_painel_exige_login_de_plataforma()
    {
        using var client = fx.PlatformClient("painel.localhost");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/tenants", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Api_interna_exige_token_de_servico()
    {
        using var client = fx.PlatformClient("conta.localhost");

        var response = await client.PostAsJsonAsync("/api/internal/invitations",
            new { tenantSlug = SineduFixture.SchoolA, email = "x@y.z", fullName = "X", privileged = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_bloqueia_apos_cinco_tentativas_erradas()
    {
        var email = await CreateAccountAsync("Senha-Correta-2026", mfa: false);
        using var client = fx.PlatformClient("conta.localhost");

        for (var i = 0; i < AccountService.MaxFailedAttempts; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "errada-errada" });
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Senha-Correta-2026" });
        Assert.Contains("account_locked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_nao_revela_se_o_email_existe()
    {
        using var client = fx.PlatformClient("conta.localhost");

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "ninguem@lugar.nenhum", password = "qualquer-coisa" });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = await CreateAccountAsync("Senha-Correta-2026", mfa: false), password = "errada-errada" });

        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadFromJsonAsync<Problem>(), await wrong.Content.ReadFromJsonAsync<Problem>());
    }

    [Fact]
    public async Task Conta_com_papel_privilegiado_precisa_de_mfa()
    {
        var email = await CreateAccountAsync("Senha-Correta-2026", mfa: false, privilegedMembership: true);
        using var client = fx.PlatformClient("conta.localhost");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Senha-Correta-2026" });

        Assert.Contains("mfa_enrollment_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Codigo_totp_nao_pode_ser_reutilizado()
    {
        var secret = TotpService.NewSecret();
        var code = new Totp(secret).ComputeTotp();

        var step = TotpService.Verify(secret, code, lastStep: null);

        Assert.NotNull(step);
        Assert.Null(TotpService.Verify(secret, code, lastStep: step));
    }

    [Theory]
    [InlineData("curta", false)]
    [InlineData("123456789012", false)]
    [InlineData("aaaaaaaaaaaaaaaa", false)]
    [InlineData("diretora-2026-ok", false)]
    [InlineData("Ipê-amarelo-floresce", true)]
    public void Politica_de_senha(string password, bool accepted) =>
        Assert.Equal(accepted, PasswordPolicy.Validate(password, "diretora@escola.local") is null);

    [Theory]
    [InlineData("http://escola-a.localhost/signin-oidc", "escola-a")]
    [InlineData("http://escola-a.localhost/outro", null)]
    [InlineData("http://escola-a.localhost/signin-oidc?x=1", null)]
    [InlineData("http://escola-a.atacante.com/signin-oidc", null)]
    [InlineData("http://painel.localhost/signin-oidc", null)]
    [InlineData("https://escola-a.localhost/signin-oidc", null)]
    public void Redirect_do_oidc_so_aceita_o_modelo_de_endereco_das_escolas(string uri, string? expected)
    {
        var urls = fx.Platform.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Sinedu.SharedKernel.Web.SineduUrls>>().Value;
        var classifier = fx.Platform.Services.GetRequiredService<Sinedu.SharedKernel.Tenancy.HostClassifier>();

        Assert.Equal(expected, Sinedu.Platform.Identity.Oidc.OidcClients.TenantSlugFromRedirect(uri, urls, classifier, "/signin-oidc"));
    }

    private async Task<string> CreateAccountAsync(string password, bool mfa, bool privilegedMembership = false)
    {
        await using var scope = fx.Platform.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = $"{Guid.NewGuid():N}@teste.local";
        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            FullName = "Conta de teste",
            PasswordHash = await PasswordHasher.HashAsync(password),
            Status = AccountStatus.Active,
            Kind = AccountKind.School,
            MfaEnabled = mfa,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        if (privilegedMembership)
        {
            var tenant = scope.ServiceProvider.GetRequiredService<TenantsDbContext>().Tenants.First(t => t.Slug == SineduFixture.SchoolA);
            db.Memberships.Add(new TenantMembership
            {
                Id = Guid.CreateVersion7(), UserId = user.Id, TenantId = tenant.Id, TenantSlug = tenant.Slug, TenantName = tenant.DisplayName,
                Active = true, Privileged = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return email;
    }

    private sealed record Problem(string? Title, int? Status, string? Code);
}
