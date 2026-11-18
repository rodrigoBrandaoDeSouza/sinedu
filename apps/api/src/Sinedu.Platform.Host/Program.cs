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
