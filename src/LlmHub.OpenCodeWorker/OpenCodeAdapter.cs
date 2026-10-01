using LlmHub.Contracts.Adapters;

namespace LlmHub.OpenCodeWorker;

public sealed class OpenCodeAdapter : IAgentAdapter
{
    public Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
        => Task.FromException<ExecutionResult>(new InvalidOperationException("OpenCode has not been configured. Complete the compatibility proof before enabling this adapter."));

    public Task CancelAsync(string runId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AdapterHealth(false, "OpenCode is not configured."));
}
