using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace Sinedu.SharedKernel.Web;

public static class WebDefaults
{
    /// <summary>
    /// Configuração comum aos dois hosts: logs estruturados sem corpo de requisição,
    /// ProblemDetails, rate limiting e opções de servidor.
    /// </summary>
    public static WebApplicationBuilder AddSineduWebDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        // Enums como texto na API (contrato legível e estável).
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ctx =>
            {
                // Nunca expor detalhes internos (stack trace, mensagens de exceção) ao cliente.
                ctx.ProblemDetails.Detail = null;
                ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier;
            });

        // Limite por IP. Configurável (RateLimiting:PermitPerMinute) para testes, onde todos os clientes têm o mesmo IP.
        var permitPerMinute = int.TryParse(builder.Configuration["RateLimiting:PermitPerMinute"], out var configured) && configured > 0 ? configured : 300;
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        return builder;
    }

    public static WebApplication UseSineduWebDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Atrás do Caddy: esquema, host e IP do cliente vêm dos cabeçalhos X-Forwarded-*, mas só da rede do proxy
        // (Sinedu:TrustedProxyNetwork, ex.: 172.18.0.0/16). Sem isso, rate limit e auditoria veriam só o IP do proxy.
        var trusted = app.Configuration["Sinedu:TrustedProxyNetwork"];
        if (!string.IsNullOrWhiteSpace(trusted))
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
                ForwardLimit = 1,
            };
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(trusted));
            app.UseForwardedHeaders(options);
        }

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseSerilogRequestLogging(o =>
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} respondeu {StatusCode} em {Elapsed:0} ms");
        app.UseRateLimiter();

        return app;
    }
}
