using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sinedu.SharedKernel.Modules;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed class ProtectedRecordsModule : IModule
{
    public string Name => "ProtectedRecords";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
