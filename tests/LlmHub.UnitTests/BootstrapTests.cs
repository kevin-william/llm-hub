namespace LlmHub.UnitTests;

public sealed class BootstrapTests
{
    [Fact]
    public void DomainAssemblyIsAvailable()
    {
        Assert.NotNull(typeof(BootstrapTests).Assembly);
    }
}
