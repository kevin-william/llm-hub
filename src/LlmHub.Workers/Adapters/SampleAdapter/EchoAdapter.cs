using System.Collections.Concurrent;
using LlmHub.Contracts.Adapters;

namespace LlmHub.Workers.Adapters.SampleAdapter;

public sealed class EchoAdapter : IAgentAdapter
{
    private readonly ConcurrentDictionary<string, byte> cancelledRuns = new(StringComparer.Ordinal);

    public Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cancelledRuns.ContainsKey(input.RunId))
        {
            throw new OperationCanceledException($"Run '{input.RunId}' was cancelled.");
        }

        return Task.FromResult(new ExecutionResult($"echo: {input.Content}", input.ArtifactReferences, input.SessionId));
    }

    public Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        cancelledRuns.TryAdd(runId, 0);
        return Task.CompletedTask;
    }

    public Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AdapterHealth(true, "Sample echo adapter is ready."));
}
