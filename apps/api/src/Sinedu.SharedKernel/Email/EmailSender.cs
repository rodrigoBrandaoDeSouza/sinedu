using System.Globalization;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.SharedKernel.Email;

/// <summary>
/// Mensagem de e-mail. <see cref="TenantId"/> faz sair pelo SMTP da escola (se configurado);
/// <see cref="Smtp"/> força uma configuração específica (área de testes do painel).
/// </summary>
public sealed record EmailMessage(string To, string Subject, string TextBody)
{
    public Guid? TenantId { get; init; }

    public SmtpOptions? Smtp { get; init; }
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "nao-responda@sinedu.local";

    public string FromName { get; set; } = "Sinedu";
}

/// <summary>SMTP próprio de uma escola, no Vault junto das credenciais dela. A senha nunca vai ao banco nem volta à tela.</summary>
public static class TenantSmtp
{
    public static string SecretPath(Guid tenantId) => $"tenants/{tenantId:N}/smtp";

    public static Dictionary<string, string> ToSecret(SmtpOptions o) => new()
    {
        ["host"] = o.Host,
        ["port"] = o.Port.ToString(CultureInfo.InvariantCulture),
        ["use_tls"] = o.UseTls ? "true" : "false",
        ["username"] = o.Username ?? "",
        ["password"] = o.Password ?? "",
        ["from_address"] = o.FromAddress,
        ["from_name"] = o.FromName,
    };

    public static SmtpOptions? FromSecret(IReadOnlyDictionary<string, string>? s) =>
        s is null || !s.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host)
            ? null
            : new SmtpOptions
            {
                Host = host,
                Port = int.TryParse(s.GetValueOrDefault("port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 587,
                UseTls = s.GetValueOrDefault("use_tls") == "true",
                Username = NullIfEmpty(s.GetValueOrDefault("username")),
                Password = NullIfEmpty(s.GetValueOrDefault("password")),
                FromAddress = s.GetValueOrDefault("from_address") ?? "nao-responda@sinedu.local",
                FromName = s.GetValueOrDefault("from_name") ?? "Sinedu",
            };

    private static string? NullIfEmpty(string? v) => string.IsNullOrEmpty(v) ? null : v;
}

/// <summary>
/// Decide por qual SMTP a mensagem sai: o informado na mensagem → o da escola (da mensagem ou da requisição atual)
/// → o padrão da plataforma. A configuração da escola fica em cache por 1 minuto.
/// </summary>
public sealed class SmtpSettingsResolver(IOptions<SmtpOptions> defaults, ISecretStore secrets, IMemoryCache cache, IServiceProvider services)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    public static string CacheKey(Guid tenantId) => $"smtp:{tenantId:N}";

    public async Task<(SmtpOptions Options, string Source)> ResolveAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Smtp is { } explicitOptions)
        {
            return (explicitOptions, "informado");
        }

        var tenantId = message.TenantId;
        if (tenantId is null && services.GetService<TenantScope>() is { HasTenant: true } scope)
        {
            tenantId = scope.Current.Id;
        }

        if (tenantId is { } id)
        {
            var own = await cache.GetOrCreateAsync(CacheKey(id), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                return TenantSmtp.FromSecret(await secrets.ReadAsync(TenantSmtp.SecretPath(id), ct));
            });
            if (own is not null)
            {
                return (own, "escola");
            }
        }

        return (defaults.Value, "padrão");
    }
}

/// <summary>Envio por SMTP genérico (D8). Mensagens nunca carregam dados de Saúde ou Proteção.</summary>
public sealed class SmtpEmailSender(SmtpSettingsResolver resolver) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (o, _) = await resolver.ResolveAsync(message, cancellationToken);

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient { Timeout = 20_000 };
        var security = o.UseTls ? (o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None;
        await client.ConnectAsync(o.Host, o.Port, security, cancellationToken);
        if (!string.IsNullOrEmpty(o.Username))
        {
            await client.AuthenticateAsync(o.Username, o.Password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
