using System.Globalization;
using System.Security.Claims;

namespace Sinedu.Platform.Identity;

public static class AccountSchemes
{
    /// <summary>Sessão do login único em conta.{domínio}.</summary>
    public const string Session = "conta";

    /// <summary>Etapa intermediária: senha validada, falta o segundo fator ou o cadastro do MFA.</summary>
    public const string Pending = "conta.pending";

    /// <summary>Sessão do painel em painel.{domínio}.</summary>
    public const string PanelCookie = "painel";

    /// <summary>Cliente OIDC do painel (o painel entra pelo login único como qualquer outro cliente).</summary>
    public const string PanelOidc = "painel.oidc";

    /// <summary>Escolhe o esquema pelo host da requisição.</summary>
    public const string HostSelector = "plataforma";
}

public static class AccountClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string AuthTime = "auth_time";
    public const string Amr = "amr";
    public const string Stamp = "stamp";
    public const string Stage = "stage";

    public const string AmrPassword = "pwd";
    public const string AmrMfa = "mfa";

    public static ClaimsPrincipal SessionPrincipal(UserAccount user, DateTimeOffset authTime, bool mfa)
    {
        var claims = new List<Claim>
        {
            new(Subject, user.Id.ToString()),
            new(Name, user.FullName),
            new(Email, user.Email),
            new(AuthTime, authTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(Stamp, user.SecurityStamp.ToString()),
            new(Amr, AmrPassword),
        };
        if (mfa)
        {
            claims.Add(new Claim(Amr, AmrMfa));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AccountSchemes.Session, Name, Role));
    }

    public static ClaimsPrincipal PendingPrincipal(UserAccount user, string stage) =>
        new(new ClaimsIdentity(
            [new Claim(Subject, user.Id.ToString()), new Claim(Stage, stage), new Claim(Stamp, user.SecurityStamp.ToString())],
            AccountSchemes.Pending));

    public static DateTimeOffset? GetAuthTime(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    public static Guid? GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(Subject), out var id) ? id : null;
}
