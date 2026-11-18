using Microsoft.EntityFrameworkCore;
using Sinedu.Platform.Billing;
using Sinedu.Platform.Host;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Metrics;
using Sinedu.Platform.Support;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Infrastructure;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Persistence;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSineduWebDefaults();

var services = builder.Services;
var configuration = builder.Configuration;
var urls = configuration.GetSection(SineduUrls.Section).Get<SineduUrls>()
    ?? throw new InvalidOperationException("Seção 'Sinedu' ausente.");
var platformConnection = configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException("ConnectionStrings:Platform ausente.");

services.AddOptions<SineduUrls>().Bind(configuration.GetSection(SineduUrls.Section));
var hostClassifier = new HostClassifier(urls.BaseDomain);
services.AddSingleton(hostClassifier);
services.AddSineduInfrastructure(configuration);
services.AddHttpContextAccessor();

services.AddDbContext<AuditDbContext>(o => o.UseSineduPostgres(platformConnection, AuditDbContext.Schema));
services.AddScoped<IAuditLog, AuditLog>();

// Migrations dos bancos de escola (DDL apenas; a plataforma não lê dados de alunos).
services.AddSingleton<ITenantSchemaMigrator, TenantSchemaMigrator>();

services.AddModules(
    configuration,
    new IdentityModule(),
    new TenantsModule(),
    new BillingModule(),
    new MetricsModule(),
    new SupportModule());

var app = builder.Build();

if (args.Contains("migrate") || configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await PlatformDatabase.MigrateAsync(app.Services);

    // `migrate` sozinho (implantação) termina aqui; combinado com outros comandos, segue.
    if (args.Length == 1 && args[0] == "migrate") return;
}

// Instalação nova: create-super-admin <e-mail> "<Nome completo>" — convite por e-mail; só se ainda não houver SUPER_ADMIN.
if (args.Length >= 3 && args[0] == "create-super-admin")
{
    await using var scope = app.Services.CreateAsyncScope();
    var id = await scope.ServiceProvider.GetRequiredService<Sinedu.Platform.Identity.AccountService>().InviteFirstSuperAdminAsync(args[1], args[2], CancellationToken.None);
    Console.WriteLine($"Convite enviado para {args[1]} (conta {id}).");
    return;
}

if (args.Contains("migrate-tenants") || args.Contains("seed-dev"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var (updated, error) = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().MigrateOutdatedAsync(CancellationToken.None);
    Console.WriteLine($"Escolas atualizadas para o schema {TenantSchema.Version}: {updated}{(error is null ? "" : $" — erro: {error}")}");
    if (!args.Contains("seed-dev")) return;
}

if (args.Contains("seed-dev"))
{
    await DevSeeder.RunAsync(app.Services);
    return;
}

app.UseSineduWebDefaults();

// Este host atende apenas conta.{domínio} e painel.{domínio}; o OIDC só existe em conta.
app.Use(async (context, next) =>
{
    var kind = hostClassifier.Classify(context.Request.Host.Value).Kind;
    var path = context.Request.Path;
    var oidcPath = path.StartsWithSegments("/connect", StringComparison.Ordinal)
        || path.StartsWithSegments("/.well-known", StringComparison.Ordinal);

    if (kind is not (HostKind.Account or HostKind.Panel) || (oidcPath && kind != HostKind.Account))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.UseMiddleware<SameOriginMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", (HttpContext context) => Results.Ok(new
{
    status = "ok",
    surface = hostClassifier.Classify(context.Request.Host.Value).Kind == HostKind.Panel ? "painel" : "conta",
}));

app.MapModules();

await app.RunAsync();

namespace Sinedu.Platform.Host
{
    internal static class PlatformDatabase
    {
        public static async Task MigrateAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<TenantsDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        }
    }
}
