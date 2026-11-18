using Sinedu.Platform.Billing;
using Sinedu.Platform.Identity;
using Sinedu.Platform.Metrics;
using Sinedu.Platform.Support;
using Sinedu.Platform.Tenants;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddSineduWebDefaults();

var baseDomain = builder.Configuration["Sinedu:BaseDomain"]
    ?? throw new InvalidOperationException("Configuração 'Sinedu:BaseDomain' ausente.");
var hostClassifier = new HostClassifier(baseDomain);
builder.Services.AddSingleton(hostClassifier);

builder.Services.AddModules(
    builder.Configuration,
    new IdentityModule(),
    new TenantsModule(),
    new BillingModule(),
    new MetricsModule(),
    new SupportModule());

var app = builder.Build();

app.UseSineduWebDefaults();

// Este host atende apenas conta.{domínio} e painel.{domínio}.
app.Use(async (context, next) =>
{
    var kind = hostClassifier.Classify(context.Request.Host.Value).Kind;
    if (kind is not (HostKind.Account or HostKind.Panel))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.MapGet("/api/health", (HttpContext context) => Results.Ok(new
{
    status = "ok",
    surface = hostClassifier.Classify(context.Request.Host.Value).Kind == HostKind.Panel ? "painel" : "conta",
}));

app.MapModules();

await app.RunAsync();
