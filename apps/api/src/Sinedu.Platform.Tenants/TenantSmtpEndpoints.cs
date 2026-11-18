using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Platform.Tenants;

public sealed record SmtpRequest(string? Host, int Port, bool UseTls, string? Username, string? Password, string? FromAddress, string? FromName);

public sealed record EmailTestRequest(Guid? TenantId, string? To, SmtpRequest? Smtp);

/// <summary>
/// SMTP por escola, configurado no painel (só SUPER_ADMIN). Fica no Vault junto das credenciais da escola;
/// a senha nunca é devolvida. Escola sem SMTP próprio usa o padrão da plataforma.
/// </summary>
internal static class TenantSmtpEndpoints
{
    public static void Map(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/{id:guid}/smtp", async (Guid id, TenantsDbContext db, ISecretStore secrets, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var o = TenantSmtp.FromSecret(await secrets.ReadAsync(TenantSmtp.SecretPath(id), ct));
            return Results.Ok(o is null
                ? new { configured = false, host = (string?)null, port = 587, useTls = true, username = (string?)null, hasPassword = false, fromAddress = (string?)null, fromName = (string?)null }
                : new { configured = true, host = (string?)o.Host, port = o.Port, useTls = o.UseTls, username = o.Username, hasPassword = !string.IsNullOrEmpty(o.Password), fromAddress = (string?)o.FromAddress, fromName = (string?)o.FromName });
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapPut("/{id:guid}/smtp", async (Guid id, SmtpRequest body, TenantsDbContext db, ISecretStore secrets, IMemoryCache cache, IAuditLog audit, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var current = TenantSmtp.FromSecret(await secrets.ReadAsync(TenantSmtp.SecretPath(id), ct));
            var (options, errors) = Validate(body, keepPassword: current?.Password);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await secrets.WriteAsync(TenantSmtp.SecretPath(id), TenantSmtp.ToSecret(options!), ct);
            cache.Remove(SmtpSettingsResolver.CacheKey(id));
            await audit.WriteAsync(new AuditEntry("tenant_smtp_updated", ResourceType: "tenant", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        tenants.MapDelete("/{id:guid}/smtp", async (Guid id, TenantsDbContext db, ISecretStore secrets, IMemoryCache cache, IAuditLog audit, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            await secrets.DeleteAsync(TenantSmtp.SecretPath(id), ct);
            cache.Remove(SmtpSettingsResolver.CacheKey(id));
            await audit.WriteAsync(new AuditEntry("tenant_smtp_removed", ResourceType: "tenant", ResourceId: id.ToString()), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSuperAdmin);

        // Área de testes: envia um e-mail de teste pelo SMTP da escola (salvo ou ainda não salvo) ou pelo padrão.
        tenants.MapPost("/email-test", async (EmailTestRequest body, TenantsDbContext db, ISecretStore secrets, IOptions<SmtpOptions> defaults,
            IEmailSender email, IAuditLog audit, CancellationToken ct) =>
        {
            var to = body.To?.Trim();
            if (to is null || !System.Net.Mail.MailAddress.TryCreate(to, out _))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Informe um e-mail de destino válido."] });
            }

            string school = "Padrão da plataforma";
            SmtpOptions options;
            string source;
            if (body.TenantId is { } tenantId)
            {
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
                if (tenant is null) return Results.NotFound();
                school = tenant.DisplayName;
                var saved = TenantSmtp.FromSecret(await secrets.ReadAsync(TenantSmtp.SecretPath(tenantId), ct));
                if (body.Smtp is { } draft)
                {
                    var (o, errors) = Validate(draft, keepPassword: saved?.Password);
                    if (errors.Count > 0) return Results.ValidationProblem(errors);
                    (options, source) = (o!, "configuração do formulário (não salva)");
                }
                else
                {
                    (options, source) = saved is null ? (defaults.Value, "padrão da plataforma (escola sem SMTP próprio)") : (saved, "SMTP da escola");
                }
            }
            else
            {
                (options, source) = (defaults.Value, "padrão da plataforma");
            }

            var stopwatch = Stopwatch.StartNew();
            string? error = null;
            try
            {
                await email.SendAsync(new EmailMessage(to, $"Teste de e-mail do Sinedu — {school}",
                    $"Este é um e-mail de teste enviado pelo painel da plataforma Sinedu.\n\nEscola: {school}\nServidor: {options.Host}:{options.Port}\n" +
                    $"Remetente: {options.FromName} <{options.FromAddress}>\nEnviado em: {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm} (UTC)\n\nSe você recebeu esta mensagem, o envio está funcionando.")
                {
                    Smtp = options,
                    TenantId = body.TenantId,
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = Describe(ex, options.Password);
            }

            await audit.WriteAsync(new AuditEntry(error is null ? "email_test_sent" : "email_test_failed", ResourceType: "tenant", ResourceId: body.TenantId?.ToString()), ct);
            return Results.Ok(new
            {
                ok = error is null,
                school,
                source,
                server = $"{options.Host}:{options.Port}",
                tls = options.UseTls,
                from = $"{options.FromName} <{options.FromAddress}>",
                elapsedMs = stopwatch.ElapsedMilliseconds,
                error,
            });
        }).RequireAuthorization(Policies.PlatformSuperAdmin);
    }

    private static (SmtpOptions? Options, Dictionary<string, string[]> Errors) Validate(SmtpRequest r, string? keepPassword)
    {
        var errors = new Dictionary<string, string[]>();
        var host = r.Host?.Trim();
        if (string.IsNullOrEmpty(host) || host.Length > 200 || host.Contains(' ', StringComparison.Ordinal)) errors["host"] = ["Informe o servidor SMTP (ex.: smtp.provedor.com.br)."];
        if (r.Port is < 1 or > 65535) errors["port"] = ["Porta inválida (normalmente 587 ou 465)."];
        var from = r.FromAddress?.Trim();
        if (from is null || !System.Net.Mail.MailAddress.TryCreate(from, out _)) errors["fromAddress"] = ["Informe o e-mail do remetente."];
        if (errors.Count > 0) return (null, errors);

        return (new SmtpOptions
        {
            Host = host!,
            Port = r.Port,
            UseTls = r.UseTls,
            Username = string.IsNullOrWhiteSpace(r.Username) ? null : r.Username.Trim(),
            // Senha em branco no formulário = manter a atual (ela nunca volta para a tela).
            Password = string.IsNullOrEmpty(r.Password) ? keepPassword : r.Password,
            FromAddress = from!,
            FromName = string.IsNullOrWhiteSpace(r.FromName) ? "Sinedu" : r.FromName.Trim(),
        }, errors);
    }

    /// <summary>Resposta do servidor para quem está configurando, sem nunca repetir a senha.</summary>
    private static string Describe(Exception ex, string? password)
    {
        var text = ex switch
        {
            MailKit.Security.AuthenticationException => "Usuário ou senha recusados pelo servidor.",
            MailKit.Net.Smtp.SmtpCommandException c => $"O servidor recusou ({(int)c.StatusCode}): {c.Message}",
            MailKit.Net.Smtp.SmtpProtocolException => "Erro de protocolo SMTP. Confira a porta e a opção de TLS.",
            MailKit.Security.SslHandshakeException => "Falha no TLS. Teste com a porta 587 (STARTTLS) ou 465 (TLS direto).",
            NotSupportedException n when n.Message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase) =>
                "O servidor não oferece TLS nesta porta. Desmarque \"Usar TLS\" (servidor de testes) ou use a porta 587/465 do provedor.",
            System.Net.Sockets.SocketException s => $"Não foi possível conectar ao servidor ({s.SocketErrorCode}).",
            TimeoutException or OperationCanceledException => "Tempo esgotado ao falar com o servidor.",
            _ => $"{ex.GetType().Name}: {ex.Message}",
        };
        if (!string.IsNullOrEmpty(password)) text = text.Replace(password, "***", StringComparison.Ordinal);
        return text.Length > 300 ? text[..300] : text;
    }
}
