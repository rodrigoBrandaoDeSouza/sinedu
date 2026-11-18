using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Sinedu.Platform.Host;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.Host;
using Sinedu.Tenant.Host.Auth;
using Testcontainers.PostgreSql;
using TenantEntity = Sinedu.Platform.Tenants.Tenant;

namespace Sinedu.IntegrationTests.Infrastructure;

/// <summary>
/// Ambiente real de duas escolas: PostgreSQL descartável (Testcontainers), banco da plataforma migrado,
/// escolas "escola-a" e "escola-b" provisionadas pelo mesmo código de produção. Vault, S3 e e-mail são fakes.
/// </summary>
public sealed class SineduFixture : IAsyncLifetime
{
    public const string SchoolA = "escola-a";
    public const string SchoolB = "escola-b";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sinedu_platform")
        .WithUsername("sinedu")
        .WithPassword("sinedu-test")
        .Build();

    public InMemoryVault Vault { get; } = new();

    public InMemoryObjectStorage Storage { get; } = new();

    public CapturingEmailSender Email { get; } = new();

    public FakePlatformMemberships PlatformMemberships { get; } = new();

    public WebApplicationFactory<PlatformHostEntryPoint> Platform { get; private set; } = null!;

    public WebApplicationFactory<TenantHostEntryPoint> School { get; private set; } = null!;

    public string PlatformConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await MigratePlatformAsync();

        Platform = new WebApplicationFactory<PlatformHostEntryPoint>().WithWebHostBuilder(b => Configure(b, platform: true));
        School = new WebApplicationFactory<TenantHostEntryPoint>().WithWebHostBuilder(b => Configure(b, platform: false));

        await ProvisionAsync(SchoolA, "Escola A (teste)");
        await ProvisionAsync(SchoolB, "Escola B (teste)");
    }

    public async Task DisposeAsync()
    {
        await School.DisposeAsync();
        await Platform.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Conexão de um papel da escola (owner ou app), com as credenciais guardadas no "Vault".</summary>
    public async Task<string> SchoolConnectionAsync(string slug, bool owner = false, string? database = null)
    {
        await using var db = PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == slug);
        var c = (await Vault.ReadAsync(tenant.SecretPath))!;
        return PersistenceExtensions.BuildConnectionString(
            c[TenantSecretKeys.Host], int.Parse(c[TenantSecretKeys.Port], CultureInfo.InvariantCulture), database ?? c[TenantSecretKeys.Database],
            owner ? c[TenantSecretKeys.OwnerUser] : c[TenantSecretKeys.AppUser], owner ? c[TenantSecretKeys.OwnerPassword] : c[TenantSecretKeys.AppPassword]);
    }

    public async Task SetTenantStatusAsync(string slug, TenantStatus status)
    {
        await using var db = PlatformDb<TenantsDbContext>(TenantsDbContext.Schema);
        await db.Tenants.Where(t => t.Slug == slug).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
        School.Services.GetRequiredService<IMemoryCache>().Remove($"tenant:{slug}");
    }

    /// <summary>
    /// Cliente HTTP autenticado numa escola: cria membro, papéis e sessão no banco da escola e gera o cookie
    /// com o mesmo formato de produção (passa pela validação real de sessão a cada requisição).
    /// </summary>
    public async Task<SchoolSession> SignInAsync(string slug, string[] roles, bool mfa = true, TimeSpan? authAge = null, DateTimeOffset? roleExpiresAt = null)
    {
        var userId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var authTime = now - (authAge ?? TimeSpan.FromMinutes(1));
        var sessionId = Guid.CreateVersion7();

        await using (var db = new AccessDbContext(new DbContextOptionsBuilder<AccessDbContext>()
            .UseSineduPostgres(await SchoolConnectionAsync(slug, owner: true), AccessDbContext.Schema).Options))
        {
            db.Members.Add(new Member { UserId = userId, Email = $"{userId:N}@{slug}.local", DisplayName = "Pessoa de teste", Status = MemberStatus.Active, CreatedAt = now });
            foreach (var role in roles)
            {
                db.Roles.Add(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, Role = role, GrantedAt = now, ExpiresAt = roleExpiresAt });
            }

            db.Sessions.Add(new UserSession { Id = sessionId, UserId = userId, CreatedAt = now, LastSeenAt = now, AuthTime = authTime, ExpiresAt = now.AddHours(10) });
            await db.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new(SchoolClaims.Subject, userId.ToString()),
            new(SchoolClaims.Name, "Pessoa de teste"),
            new(SchoolClaims.Email, $"{userId:N}@{slug}.local"),
            new(SchoolClaims.Tenant, slug),
            new(SchoolClaims.Session, sessionId.ToString()),
            new(SchoolClaims.AuthTime, authTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(SchoolClaims.Amr, "pwd"),
        };
        if (mfa)
        {
            claims.Add(new Claim(SchoolClaims.Amr, "mfa"));
        }

        var options = School.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SchoolSchemes.Cookie);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchoolSchemes.Cookie, SchoolClaims.Name, SchoolClaims.Role)),
            new AuthenticationProperties { ExpiresUtc = now.AddMinutes(30) },
            SchoolSchemes.Cookie);

        return new SchoolSession(userId, sessionId, slug, $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    public HttpClient SchoolClient(string slug, SchoolSession? session = null)
    {
        var client = School.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Host = $"{slug}.localhost";
        client.DefaultRequestHeaders.Add("Origin", $"http://{slug}.localhost");
        if (session is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", session.Cookie);
        }

        return client;
    }

    public HttpClient PlatformClient(string host)
    {
        var client = Platform.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    public T PlatformDb<T>(string schema)
        where T : DbContext =>
        (T)Activator.CreateInstance(typeof(T), new DbContextOptionsBuilder<T>().UseSineduPostgres(PlatformConnectionString, schema).Options)!;

    private async Task MigratePlatformAsync()
    {
        await using (var audit = PlatformDb<AuditDbContext>(AuditDbContext.Schema)) await audit.Database.MigrateAsync();
        await using (var tenants = PlatformDb<TenantsDbContext>(TenantsDbContext.Schema)) await tenants.Database.MigrateAsync();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSineduPostgres(PlatformConnectionString, IdentityDbContext.Schema).UseOpenIddict().Options);
        await identity.Database.MigrateAsync();
    }

    private async Task ProvisionAsync(string slug, string name)
    {
        await using var scope = Platform.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantsDbContext>();
        var tenant = TenantEntity.Create(Guid.CreateVersion7(), slug, name, $"diretor@{slug}.local", "Diretor de teste", DateTimeOffset.UtcNow);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(tenant.Id, CancellationToken.None);
        await db.Entry(tenant).ReloadAsync();
        if (tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException($"Falha ao provisionar {slug}: {tenant.StatusReason}");
        }
    }

    private void Configure(IWebHostBuilder builder, bool platform)
    {
        var admin = new NpgsqlConnectionStringBuilder(PlatformConnectionString) { Database = "postgres" };
        var settings = new Dictionary<string, string>
        {
            ["Sinedu:BaseDomain"] = "localhost",
            ["Sinedu:AccountUrl"] = "http://conta.localhost",
            ["Sinedu:PanelUrl"] = "http://painel.localhost",
            ["Sinedu:TenantUrlTemplate"] = "http://{slug}.localhost",
            ["Sinedu:SecureCookies"] = "false",
            ["ConnectionStrings:Platform"] = PlatformConnectionString,
            ["ConnectionStrings:PlatformRegistry"] = PlatformConnectionString,
            ["TenantDatabases:AdminConnectionString"] = admin.ConnectionString,
            ["TenantDatabases:Host"] = _postgres.Hostname,
            ["TenantDatabases:Port"] = _postgres.GetMappedPublicPort(5432).ToString(CultureInfo.InvariantCulture),
            ["Database:MigrateOnStartup"] = "false",
            ["Oidc:TenantClientSecret"] = "test-escola-client-secret-0123456789abcdef",
            ["Oidc:PanelClientSecret"] = "test-painel-client-secret-0123456789abcdef",
        };

        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataKeyProvider>().AddSingleton<IDataKeyProvider>(Vault);
            services.RemoveAll<IKeyManagement>().AddSingleton<IKeyManagement>(Vault);
            services.RemoveAll<ISecretCipher>().AddSingleton<ISecretCipher>(Vault);
            services.RemoveAll<ISecretStore>().AddSingleton<ISecretStore>(Vault);
            services.RemoveAll<IObjectStorage>().AddSingleton<IObjectStorage>(Storage);
            services.RemoveAll<IEmailSender>().AddSingleton<IEmailSender>(Email);
            if (!platform)
            {
                services.RemoveAll<IPlatformMemberships>().AddSingleton<IPlatformMemberships>(PlatformMemberships);

                // Chamadas da escola ao login único vão para o servidor de teste da plataforma (descoberta OIDC real).
                services.Configure<OpenIdConnectOptions>(SchoolSchemes.Oidc, o => o.BackchannelHttpHandler = Platform.Server.CreateHandler());
            }
        });
    }
}

public sealed record SchoolSession(Guid UserId, Guid SessionId, string Slug, string Cookie);

[CollectionDefinition(Name)]
public sealed class SineduTestGroup : ICollectionFixture<SineduFixture>
{
    public const string Name = "sinedu";
}
