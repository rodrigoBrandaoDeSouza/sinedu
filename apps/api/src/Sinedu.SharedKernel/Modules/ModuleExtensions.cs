using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sinedu.SharedKernel.Modules;

public static class ModuleExtensions
{
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var modules = endpoints.ServiceProvider.GetRequiredService<IReadOnlyList<IModule>>();
        foreach (var module in modules)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
