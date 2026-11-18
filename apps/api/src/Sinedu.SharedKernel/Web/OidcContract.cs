namespace Sinedu.SharedKernel.Web;

/// <summary>Contrato OIDC entre o login único (conta.) e as escolas.</summary>
public static class OidcContract
{
    /// <summary>Cliente único usado por todas as escolas; o redirect_uri identifica a escola.</summary>
    public const string TenantClientId = "sinedu-escola";

    public const string PanelClientId = "sinedu-painel";

    /// <summary>Escopo da API interna usada pelas escolas para sincronizar vínculos e convites.</summary>
    public const string MembershipsScope = "sinedu.vinculos";

    public const string CallbackPath = "/signin-oidc";

    public const string SignedOutPath = "/signout-callback-oidc";

    /// <summary>Janela do step-up: Saúde e Proteção exigem autenticação forte nos últimos 15 minutos (D6).</summary>
    public static readonly TimeSpan StepUpWindow = TimeSpan.FromMinutes(15);
}
