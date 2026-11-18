using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Crypto;
using Sinedu.SharedKernel.Email;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Storage;

namespace Sinedu.SharedKernel.Infrastructure;

public static class InfrastructureExtensions
{
    /// <summary>Vault, criptografia por envelope, S3 e SMTP — comuns aos dois hosts.</summary>
    public static IServiceCollection AddSineduInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<VaultOptions>().Bind(configuration.GetSection(VaultOptions.Section));
        services.AddHttpClient<VaultClient>();
        services.AddTransient<IDataKeyProvider>(sp => sp.GetRequiredService<VaultClient>());
        services.AddTransient<IKeyManagement>(sp => sp.GetRequiredService<VaultClient>());
        services.AddTransient<ISecretCipher>(sp => sp.GetRequiredService<VaultClient>());
        services.AddTransient<ISecretStore>(sp => sp.GetRequiredService<VaultClient>());
        services.AddTransient<IEnvelopeCipher, EnvelopeCipher>();

        services.AddOptions<S3Options>().Bind(configuration.GetSection(S3Options.Section));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.Section));
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
