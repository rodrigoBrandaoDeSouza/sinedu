using System.Text.RegularExpressions;

namespace Sinedu.SharedKernel.Tenancy;

public enum HostKind
{
    /// <summary>Host não pertence ao domínio base ou é inválido.</summary>
    Unknown,

    /// <summary>conta.{domínio} — login único (OIDC).</summary>
    Account,

    /// <summary>painel.{domínio} — painel gerencial da plataforma.</summary>
    Panel,

    /// <summary>Subdomínio reservado sem uso pela aplicação (www, api, ...).</summary>
    Reserved,

    /// <summary>{escola}.{domínio} — aplicação de uma escola.</summary>
    Tenant,
}

public readonly record struct HostClassification(HostKind Kind, string? TenantSlug)
{
    public static readonly HostClassification Unknown = new(HostKind.Unknown, null);
}

/// <summary>
/// Classifica o host de uma requisição (ex.: "escola-x.sinedu.com.br") em relação ao domínio base.
/// Não consulta o registro de escolas — só valida formato e subdomínios reservados.
/// </summary>
public sealed partial class HostClassifier
{
    public const string AccountSubdomain = "conta";
    public const string PanelSubdomain = "painel";

    public static readonly IReadOnlySet<string> ReservedSubdomains = new HashSet<string>(StringComparer.Ordinal)
    {
        AccountSubdomain, PanelSubdomain, "api", "www", "admin", "app", "mail", "status", "suporte",
    };

    private readonly string _baseDomain;

    public HostClassifier(string baseDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDomain);
        _baseDomain = baseDomain.Trim().TrimStart('.').ToLowerInvariant();
    }

    public HostClassification Classify(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return HostClassification.Unknown;
        }

        var hostname = StripPort(host.Trim()).ToLowerInvariant();
        var suffix = "." + _baseDomain;

        if (!hostname.EndsWith(suffix, StringComparison.Ordinal))
        {
            return HostClassification.Unknown;
        }

        var label = hostname[..^suffix.Length];

        // Só um nível de subdomínio é aceito: "a.b.sinedu.com.br" não é uma escola.
        if (label.Length == 0 || label.Contains('.', StringComparison.Ordinal))
        {
            return HostClassification.Unknown;
        }

        return label switch
        {
            AccountSubdomain => new(HostKind.Account, null),
            PanelSubdomain => new(HostKind.Panel, null),
            _ when ReservedSubdomains.Contains(label) => new(HostKind.Reserved, null),
            _ when IsValidSlug(label) => new(HostKind.Tenant, label),
            _ => HostClassification.Unknown,
        };
    }

    /// <summary>3 a 63 caracteres, letras minúsculas, dígitos e hífen, sem hífen nas pontas ou duplicado.</summary>
    public static bool IsValidSlug(string slug) =>
        slug.Length is >= 3 and <= 63
        && SlugRegex().IsMatch(slug)
        && !slug.Contains("--", StringComparison.Ordinal);

    private static string StripPort(string host)
    {
        var colon = host.LastIndexOf(':');
        return colon > 0 && !host.EndsWith(']') ? host[..colon] : host;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
