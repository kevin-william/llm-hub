using LlmHub.Contracts.Adapters;
using LlmHub.OpenCodeWorker;

namespace LlmHub.UnitTests.Adapters;

public sealed class OpenCodeAdapterTests
{
    [Fact]
    public async Task UnconfiguredAdapterReportsUnhealthyAndDoesNotExecute()
    {
        var adapter = new OpenCodeAdapter();

        var health = await adapter.HealthAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(
            new ExecutionInput("run_1", "chn_1", "prompt", null, []),
            CancellationToken.None));

        Assert.False(health.IsHealthy);
    }
}
