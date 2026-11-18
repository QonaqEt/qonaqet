using System.Reflection;

namespace Qonaqet.ArchitectureTests;

/// <summary>
/// Asılılıq istiqaməti: Domain ← Application ← Infrastructure ← hostlar (E01-T01).
/// Tam arxitektura test dəsti (mənbə skanı, hard delete qadağası və s.) E01-T14-dədir.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly SharedKernel = typeof(SharedKernel.SharedKernelAssembly).Assembly;
    private static readonly Assembly Domain = typeof(Domain.DomainAssembly).Assembly;
    private static readonly Assembly Contracts = typeof(Contracts.ContractsAssembly).Assembly;
    private static readonly Assembly Application = typeof(Application.ApplicationAssembly).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;

    public static TheoryData<string, string[]> ForbiddenReferences => new()
    {
        { "Qonaqet.SharedKernel", ["Qonaqet.Domain", "Qonaqet.Contracts", "Qonaqet.Application", "Qonaqet.Infrastructure", "Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker"] },
        { "Qonaqet.Contracts", ["Qonaqet.SharedKernel", "Qonaqet.Domain", "Qonaqet.Application", "Qonaqet.Infrastructure", "Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker"] },
        { "Qonaqet.Domain", ["Qonaqet.Contracts", "Qonaqet.Application", "Qonaqet.Infrastructure", "Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker"] },
        { "Qonaqet.Application", ["Qonaqet.Infrastructure", "Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker"] },
        { "Qonaqet.Infrastructure", ["Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker"] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Layer_does_not_reference_outer_layers(string assemblyName, string[] forbidden)
    {
        var assembly = Load(assemblyName);

        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => forbidden.Contains(name))
            .ToArray();

        Assert.True(violations.Length == 0, $"{assemblyName} qadağan olunmuş layer-lərə istinad edir: {string.Join(", ", violations)}");
    }

    [Theory]
    [InlineData("Qonaqet.Domain")]
    [InlineData("Qonaqet.SharedKernel")]
    [InlineData("Qonaqet.Contracts")]
    public void Pure_layers_reference_only_the_base_library_and_own_kernel(string assemblyName)
    {
        var assembly = Load(assemblyName);

        var external = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsBaseLibrary(name) && name != "Qonaqet.SharedKernel")
            .ToArray();

        Assert.True(external.Length == 0, $"{assemblyName} xarici paketlərə istinad edir: {string.Join(", ", external)}");
    }

    [Fact]
    public void Hosts_reference_infrastructure()
    {
        foreach (var host in new[] { "Qonaqet.Api", "Qonaqet.Integration.Api", "Qonaqet.Worker" })
        {
            var refs = Load(host).GetReferencedAssemblies().Select(a => a.Name);
            Assert.Contains("Qonaqet.Infrastructure", refs);
        }
    }

    private static bool IsBaseLibrary(string name) =>
        name is "System.Runtime" or "netstandard" or "mscorlib" or "System.Private.CoreLib"
        || name.StartsWith("System.", StringComparison.Ordinal);

    private static Assembly Load(string name) => name switch
    {
        "Qonaqet.SharedKernel" => SharedKernel,
        "Qonaqet.Domain" => Domain,
        "Qonaqet.Contracts" => Contracts,
        "Qonaqet.Application" => Application,
        "Qonaqet.Infrastructure" => Infrastructure,
        _ => Assembly.Load(name),
    };
}
