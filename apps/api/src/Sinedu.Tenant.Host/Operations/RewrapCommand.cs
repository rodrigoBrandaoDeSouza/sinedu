using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.ProtectedRecords;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// <c>rewrap-protected {escola}</c>: depois de rotacionar a chave da escola no Vault, recifra as DEKs dos registros
/// protegidos com a versão nova. Não decifra conteúdo. Ver .doc/arquitetura/runbook-rotacao-de-chave.md.
/// </summary>
internal static class RewrapCommand
{
    public static async Task RunAsync(IServiceProvider root, string slug)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        if (resolution.Status != TenantResolutionStatus.Ready)
        {
            throw new InvalidOperationException($"Escola '{slug}' indisponível: {resolution.Status}.");
        }

        sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
        var result = await sp.GetRequiredService<KeyRewrap>().RunAsync(CancellationToken.None);
        Console.WriteLine($"Escola {slug}: {result.Versions} versões, {result.Attachments} anexos e {result.Exports} exportações recifrados com a chave atual.");
    }
}
