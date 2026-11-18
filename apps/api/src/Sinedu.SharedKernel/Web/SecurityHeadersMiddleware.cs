using Microsoft.AspNetCore.Http;

namespace Sinedu.SharedKernel.Web;

/// <summary>
/// Cabeçalhos de segurança aplicados a toda resposta da API.
/// Respostas da API nunca são cacheadas nem indexadas.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.CacheControl = "no-store";
            headers.Pragma = "no-cache";
            // same-origin: o navegador manda Origin/Referer só para o próprio site (a proteção CSRF depende disso)
            // e nada para sites externos — URLs com identificadores nunca vazam. no-referrer faria o POST sair com Origin: null.
            headers["Referrer-Policy"] = "same-origin";
            headers["X-Robots-Tag"] = "noindex, nofollow";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.Remove("Server");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
