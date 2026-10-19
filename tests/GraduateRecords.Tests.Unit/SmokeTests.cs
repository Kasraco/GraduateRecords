namespace GraduateRecords.Tests.Unit;

public sealed class SmokeTests
{
    [Fact]
    public void Domain_and_Application_assemblies_load()
    {
        Assert.NotNull(typeof(Domain.AssemblyMarker).Assembly.FullName);
        Assert.NotNull(typeof(Application.AssemblyMarker).Assembly.FullName);
    }
}
