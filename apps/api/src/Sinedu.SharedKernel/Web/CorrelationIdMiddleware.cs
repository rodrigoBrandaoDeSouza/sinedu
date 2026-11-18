using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Sinedu.SharedKernel.Web;

/// <summary>
/// Gera um identificador por requisição, devolvido no cabeçalho e anexado a todos os logs.
/// Não aceita o valor do cliente para evitar injeção em logs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Guid.CreateVersion7().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
