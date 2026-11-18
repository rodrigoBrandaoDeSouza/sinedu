using Microsoft.AspNetCore.Http;

namespace Sinedu.SharedKernel.Web;

/// <summary>
/// Proteção contra CSRF para requisições com cookie: métodos que alteram estado precisam vir do mesmo
/// host (Origin/Referer) e não podem ser cross-site (Sec-Fetch-Site). Chamadas serviço→serviço,
/// sem cookie, usam token Bearer e não passam por esta checagem.
/// </summary>
public sealed class SameOriginMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
            || request.Headers.Cookie.Count == 0)
        {
            return next(context);
        }

        if (request.Headers["Sec-Fetch-Site"].ToString() is "cross-site")
        {
            return Reject(context);
        }

        var source = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(source) || source == "null")
        {
            source = request.Headers.Referer.ToString();
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var origin)
            || !string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(context);
        }

        return next(context);
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
