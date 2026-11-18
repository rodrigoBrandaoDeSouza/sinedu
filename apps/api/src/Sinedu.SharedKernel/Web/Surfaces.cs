using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sinedu.SharedKernel.Web;

/// <summary>Superfícies do Platform.Host: endpoints só respondem no host correto.</summary>
public static class Surfaces
{
    /// <summary>API do painel gerencial (painel.{domínio}/api). Host errado → 404 antes da autenticação.</summary>
    public static RouteGroupBuilder MapPanelApi(this IEndpointRouteBuilder endpoints, string prefix) =>
        endpoints.MapGroup("/api" + prefix).RequireSurface(endpoints, u => u.PanelUrl);

    /// <summary>API da conta/login único (conta.{domínio}/api).</summary>
    public static RouteGroupBuilder MapAccountApi(this IEndpointRouteBuilder endpoints, string prefix) =>
        endpoints.MapGroup("/api" + prefix).RequireSurface(endpoints, u => u.AccountUrl);

    /// <summary>Restringe pelo host no roteamento (aceita qualquer porta, para dev e proxies).</summary>
    public static TBuilder RequireSurface<TBuilder>(this TBuilder builder, IEndpointRouteBuilder endpoints, Func<SineduUrls, Uri> surface)
        where TBuilder : IEndpointConventionBuilder
    {
        var host = surface(endpoints.ServiceProvider.GetRequiredService<IOptions<SineduUrls>>().Value).Host;
        return builder.RequireHost(host, host + ":*");
    }
}

/// <summary>Nomes de políticas de autorização compartilhados entre módulos.</summary>
public static class Policies
{
    // Plataforma (painel)
    public const string PlatformSuperAdmin = "platform:super-admin";
    public const string PlatformStaff = "platform:staff";

    // Escola
    public const string SchoolMember = "school:member";
    public const string SchoolManager = "school:manager";
    public const string SchoolConfig = "school:config";
    public const string PedagogicalStaff = "school:pedagogical";
    public const string SchoolTeam = "school:team";
    public const string StudentReader = "school:student-reader";
    public const string ImportCommon = "school:import";
    public const string RecentStrongAuth = "school:recent-strong-auth";
}

/// <summary>Papéis.</summary>
public static class Roles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Support = "SUPORTE";

    public const string Director = "DIRETOR";
    public const string Pedagogue = "PEDAGOGO";
    public const string TechAdmin = "ADMIN_TECNICO";
    public const string Teacher = "PROFESSOR";
    public const string ReadOnly = "CONSULTA";
    public const string PlatformSupport = "SUPORTE_PLATAFORMA";

    public static readonly IReadOnlySet<string> SchoolRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        Director, Pedagogue, TechAdmin, Teacher, ReadOnly, PlatformSupport,
    };

    /// <summary>Papéis que exigem MFA (D6).</summary>
    public static readonly IReadOnlySet<string> Privileged = new HashSet<string>(StringComparer.Ordinal)
    {
        Director, Pedagogue, TechAdmin, PlatformSupport,
    };
}
