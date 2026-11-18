namespace Qonaqet.Application.Tests;

public sealed class ApplicationAssemblyTests
{
    [Fact]
    public void Application_assembly_is_named_by_convention()
    {
        Assert.Equal("Qonaqet.Application", typeof(ApplicationAssembly).Assembly.GetName().Name);
    }
}
