using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sinedu.SharedKernel.Modules;

/// <summary>
/// Contrato de um módulo do monólito. Cada módulo registra seus serviços e endpoints
/// sem conhecer os detalhes internos dos demais.
/// </summary>
public interface IModule
{
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
