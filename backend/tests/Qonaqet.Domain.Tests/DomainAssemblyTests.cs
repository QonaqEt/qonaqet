namespace Qonaqet.Domain.Tests;

public sealed class DomainAssemblyTests
{
    [Fact]
    public void Domain_assembly_is_named_by_convention()
    {
        Assert.Equal("Qonaqet.Domain", typeof(DomainAssembly).Assembly.GetName().Name);
    }
}
