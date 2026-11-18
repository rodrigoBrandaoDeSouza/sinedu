using Microsoft.EntityFrameworkCore;
using OtpNet;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Identity.Security;
using Sinedu.Platform.Tenants;
using TenantEntity = Sinedu.Platform.Tenants.Tenant;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Host;

/// <summary>
/// Dados de DESENVOLVIMENTO: super admin, uma rede e duas escolas fictícias com seus diretores.
/// Senhas e segredos TOTP são fixos e públicos — nunca rodar fora do ambiente local.
/// Uso: dotnet run --project src/Sinedu.Platform.Host -- migrate seed-dev
/// </summary>
internal static class DevSeeder
{
    public const string Password = "Sinedu-Dev-2026!";

    /// <summary>Segredo TOTP (base32) de todas as contas de dev. Gere o código com qualquer app autenticador.</summary>
    public const string TotpSecret = "KRSXG5CTMVRXEZLUKNSWG4TFOQ2DAMZR";

    private static readonly (string Slug, string Name, string DirectorEmail, string DirectorName)[] Schools =
    [
        ("escola-demo", "Colégio Estadual Demonstração", "diretora@escola-demo.local", "Diretora Fictícia"),
        ("escola-modelo", "Escola Municipal Modelo", "diretor@escola-modelo.local", "Diretor Fictício"),
    ];

    public static async Task RunAsync(IServiceProvider root)
    {
        var environment = root.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("seed-dev só pode rodar em Development.");
        }

        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var identity = sp.GetRequiredService<IdentityDbContext>();
        var tenants = sp.GetRequiredService<TenantsDbContext>();
        var totp = sp.GetRequiredService<TotpService>();
        var now = DateTimeOffset.UtcNow;

        var admin = await EnsureAccountAsync(identity, totp, "admin@sinedu.local", "Super Admin (dev)", AccountKind.Platform, now);
        if (!await identity.PlatformRoles.AnyAsync(r => r.UserId == admin.Id && r.RevokedAt == null))
        {
            identity.PlatformRoles.Add(new PlatformRoleGrant { Id = Guid.CreateVersion7(), UserId = admin.Id, Role = Roles.SuperAdmin, GrantedAt = now });
            await identity.SaveChangesAsync();
        }

        var network = await tenants.Networks.FirstOrDefaultAsync(n => n.Name == "Rede Fictícia de Desenvolvimento");
        if (network is null)
        {
            network = new Network { Id = Guid.CreateVersion7(), Name = "Rede Fictícia de Desenvolvimento", Kind = NetworkKind.Estadual, Uf = "PR", CreatedAt = now };
            tenants.Networks.Add(network);
            await tenants.SaveChangesAsync();
        }

        foreach (var (slug, name, directorEmail, directorName) in Schools)
        {
            var tenant = await tenants.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
            if (tenant is null)
            {
                tenant = TenantEntity.Create(Guid.CreateVersion7(), slug, name, directorEmail, directorName, now);
                tenant.NetworkId = network.Id;
                tenants.Tenants.Add(tenant);
                await tenants.SaveChangesAsync();
            }

            await sp.GetRequiredService<TenantProvisioner>().ProvisionAsync(tenant.Id, CancellationToken.None);
            await tenants.Entry(tenant).ReloadAsync();
            Console.WriteLine($"  escola {slug}: {tenant.Status} {tenant.StatusReason}");

            // O provisionamento convida o diretor; em dev ativamos a conta direto com senha e MFA conhecidos.
            identity.ChangeTracker.Clear();
            await EnsureAccountAsync(identity, totp, directorEmail, directorName, AccountKind.School, now);
        }

        var urls = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SineduUrls>>().Value;
        Console.WriteLine();
        Console.WriteLine("Contas de desenvolvimento (senha para todas: " + Password + ")");
        Console.WriteLine("  admin@sinedu.local            → " + urls.PanelUrl);
        foreach (var school in Schools)
        {
            Console.WriteLine($"  {school.DirectorEmail,-29} → {urls.TenantUrl(school.Slug)}");
        }

        Console.WriteLine("Segredo TOTP (base32) de todas as contas: " + TotpSecret);
        Console.WriteLine("Código TOTP agora: " + new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp());
    }

    private static async Task<UserAccount> EnsureAccountAsync(
        IdentityDbContext db, TotpService totp, string email, string name, AccountKind kind, DateTimeOffset now)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new UserAccount { Id = Guid.CreateVersion7(), Email = email, FullName = name, Kind = kind, SecurityStamp = Guid.NewGuid(), CreatedAt = now };
            db.Users.Add(user);
        }

        if (user.Status != AccountStatus.Active)
        {
            user.PasswordHash = await PasswordHasher.HashAsync(Password);
            user.Status = AccountStatus.Active;
            user.TotpSecretEncrypted = await totp.ProtectAsync(Base32Encoding.ToBytes(TotpSecret), CancellationToken.None);
            user.MfaEnabled = true;
        }

        await db.SaveChangesAsync();
        return user;
    }
}
