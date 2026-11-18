using Npgsql;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Infrastructure;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.Analytics;
using Sinedu.Tenant.Host.Auth;
using Sinedu.Tenant.Host.Tenancy;
using Sinedu.Tenant.Imports;
using Sinedu.Tenant.Notifications;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.ProtectedRecords;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

var builder = WebApplication.CreateBuilder(args);

builder.AddSineduWebDefaults();

var services = builder.Services;
var configuration = builder.Configuration;
var urls = configuration.GetSection(SineduUrls.Section).Get<SineduUrls>()
    ?? throw new InvalidOperationException("Seção 'Sinedu' ausente.");

services.AddOptions<SineduUrls>().Bind(configuration.GetSection(SineduUrls.Section));
services.AddSingleton(new HostClassifier(urls.BaseDomain));
services.AddSineduInfrastructure(configuration);
services.AddHttpContextAccessor();
services.AddMemoryCache();

// Escola da requisição: registro (somente leitura) + credenciais do Vault.
services.AddScoped<TenantScope>();
services.AddSingleton(new RegistryDataSource(NpgsqlDataSource.Create(
    configuration.GetConnectionString("PlatformRegistry") ?? throw new InvalidOperationException("ConnectionStrings:PlatformRegistry ausente."))));
services.AddScoped<ITenantResolver, TenantRegistryResolver>();
services.AddHostedService<Sinedu.Tenant.Host.Operations.NotificationRetryWorker>();

services.AddTenantDbContext<AuditDbContext>(AuditDbContext.Schema);
services.AddScoped<IAuditLog, AuditLog>();

services.AddSchoolAuthentication();

services.AddModules(
    configuration,
    new AccessModule(),
    new SchoolModule(),
    new StudentsModule(),
    new ProtectedRecordsModule(),
    new OccurrencesModule(),
    new NotificationsModule(),
    new ImportsModule(),
    new AnalyticsModule());

var app = builder.Build();

if (args.Length >= 2 && args[0] == "rewrap-protected")
{
    await Sinedu.Tenant.Host.Operations.RewrapCommand.RunAsync(app.Services, args[1]);
    return;
}

if (args.Length >= 3 && args[0] == "import-sample")
{
    await Sinedu.Tenant.Host.Dev.SampleImporter.RunAsync(app.Services, args[1], args[2], args.Length > 3 ? args[3] : null);
    return;
}

app.UseSineduWebDefaults();

// Toda requisição precisa de uma escola ativa resolvida pelo subdomínio — antes da autenticação,
// porque a sessão é validada no banco da escola.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<SameOriginMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", (TenantScope tenant) => Results.Ok(new { status = "ok", tenant = tenant.Current.Slug }))
    .AllowAnonymous();

SessionEndpoints.Map(app);
app.MapModules();

await app.RunAsync();
