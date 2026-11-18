namespace Sinedu.Tenant.Imports;

/// <summary>Metadados obrigatórios de um conector (spec §7.2).</summary>
public sealed record ConnectorDescriptor(
    string Id,
    string Name,
    string Version,
    string Scope,
    string Authentication,
    string Availability,
    string Limits,
    string UsagePolicy);

/// <summary>
/// Conector autorizado com sistema oficial (ex.: SERE/PR). NENHUMA implementação existe no MVP:
/// só pode ser criada com documentação/convênio/permissão formal, sem contornar login, captcha ou MFA,
/// e sem guardar senhas em texto puro (credenciais no Vault, com expiração). Sem acesso formal, vale o upload.
/// </summary>
public interface ISchoolDataConnector
{
    ConnectorDescriptor Descriptor { get; }

    /// <summary>Entrega um arquivo no mesmo formato aceito pelo upload, que segue o fluxo normal (mapeamento, prévia, revisão).</summary>
    Task<(Stream Content, string FileName)> FetchAsync(CancellationToken cancellationToken);
}
