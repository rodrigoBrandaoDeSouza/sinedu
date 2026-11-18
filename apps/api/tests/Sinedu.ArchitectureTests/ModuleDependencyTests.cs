using System.Reflection;
using NetArchTest.Rules;
using Sinedu.Platform.Host;
using Sinedu.Tenant.Host;

namespace Sinedu.ArchitectureTests;

/// <summary>
/// Regras de dependência da arquitetura (.doc/arquitetura/visao-tecnica.md §3).
/// </summary>
public class ModuleDependencyTests
{
    private static readonly Assembly[] PlatformModules = LoadModules(typeof(PlatformHostEntryPoint).Assembly, "Sinedu.Platform.");
    private static readonly Assembly[] TenantModules = LoadModules(typeof(TenantHostEntryPoint).Assembly, "Sinedu.Tenant.");

    [Fact]
    public void Existem_os_modulos_esperados()
    {
        Assert.Equal(5, PlatformModules.Length);
        Assert.Equal(8, TenantModules.Length);
    }

    [Fact]
    public void Nenhum_modulo_depende_de_ProtectedRecords()
    {
        var others = TenantModules.Concat(PlatformModules)
            .Where(a => a.GetName().Name != "Sinedu.Tenant.ProtectedRecords")
            .ToArray();

        var result = Types.InAssemblies(others)
            .ShouldNot().HaveDependencyOn("Sinedu.Tenant.ProtectedRecords")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Modulos_da_plataforma_nao_dependem_de_modulos_da_escola()
    {
        var result = Types.InAssemblies(PlatformModules)
            .ShouldNot().HaveDependencyOnAny(TenantModules.Select(a => a.GetName().Name!).ToArray())
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Modulos_da_escola_nao_dependem_de_modulos_da_plataforma()
    {
        var result = Types.InAssemblies(TenantModules)
            .ShouldNot().HaveDependencyOnAny(PlatformModules.Select(a => a.GetName().Name!).ToArray())
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void SharedKernel_nao_depende_de_nenhum_modulo()
    {
        var result = Types.InAssembly(typeof(Sinedu.SharedKernel.Modules.IModule).Assembly)
            .ShouldNot().HaveDependencyOnAny("Sinedu.Platform", "Sinedu.Tenant")
            .GetResult();

        AssertSuccessful(result);
    }

    private static Assembly[] LoadModules(Assembly host, string prefix) =>
        host.GetReferencedAssemblies()
            .Where(n => n.Name!.StartsWith(prefix, StringComparison.Ordinal) && !n.Name.EndsWith(".Host", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .ToArray();

    private static void AssertSuccessful(TestResult result) =>
        Assert.True(result.IsSuccessful, "Dependências proibidas: " + string.Join(", ", result.FailingTypeNames ?? []));
}
