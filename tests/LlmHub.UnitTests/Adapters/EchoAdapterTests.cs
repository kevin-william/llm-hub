using LlmHub.Contracts.Adapters;
using LlmHub.Contracts.Artifacts;
using LlmHub.Workers.Adapters.SampleAdapter;

namespace LlmHub.UnitTests.Adapters;

public sealed class EchoAdapterTests
{
    [Fact]
    public async Task AdapterExecutesWithTheSharedContractAndPreservesSessionAndArtifacts()
    {
        var adapter = new EchoAdapter();
        var artifact = new ArtifactReference($"sha256/{new string('b', 64)}", new string('b', 64), "text/plain", 4);
        var result = await adapter.ExecuteAsync(new ExecutionInput("run_echo", "chn_echo", "hello", null, [artifact], "session_echo"), CancellationToken.None);

        Assert.True((await adapter.HealthAsync(CancellationToken.None)).IsHealthy);
        Assert.Equal("echo: hello", result.Content);
        Assert.Equal("session_echo", result.SessionId);
        Assert.Equal([artifact], result.ArtifactReferences);
    }

    [Fact]
    public async Task CancelledRunDoesNotExecute()
    {
        var adapter = new EchoAdapter();
        await adapter.CancelAsync("run_cancel", CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ExecuteAsync(new ExecutionInput("run_cancel", "chn_echo", "hello", null, []), CancellationToken.None));
    }
}
