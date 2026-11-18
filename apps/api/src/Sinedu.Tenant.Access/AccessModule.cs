using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;

namespace Sinedu.Tenant.Access;

public sealed class AccessModule : IModule
{
    public string Name => "Access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
