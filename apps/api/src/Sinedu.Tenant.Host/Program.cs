using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.Analytics;
using Sinedu.Tenant.Imports;
using Sinedu.Tenant.Notifications;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.ProtectedRecords;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

var builder = WebApplication.CreateBuilder(args);

builder.AddSineduWebDefaults();

var baseDomain = builder.Configuration["Sinedu:BaseDomain"]
    ?? throw new InvalidOperationException("Configuração 'Sinedu:BaseDomain' ausente.");
builder.Services.AddSingleton(new HostClassifier(baseDomain));

builder.Services.AddModules(
    builder.Configuration,
    new AccessModule(),
    new SchoolModule(),
    new StudentsModule(),
    new ProtectedRecordsModule(),
    new OccurrencesModule(),
    new NotificationsModule(),
    new ImportsModule(),
    new AnalyticsModule());

var app = builder.Build();

app.UseSineduWebDefaults();

// Toda requisição precisa de uma escola resolvida pelo subdomínio.
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapGet("/api/health", (HttpContext context) => Results.Ok(new
{
    status = "ok",
    tenant = context.GetTenant().Slug,
}));

app.MapModules();

await app.RunAsync();
