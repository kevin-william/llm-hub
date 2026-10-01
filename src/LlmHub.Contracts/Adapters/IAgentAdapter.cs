using LlmHub.Contracts.Artifacts;

namespace LlmHub.Contracts.Adapters;

public sealed record ExecutionInput(string RunId, string ChannelId, string Content, DateTimeOffset? Deadline, IReadOnlyList<ArtifactReference> ArtifactReferences, string? SessionId = null);

public sealed record ExecutionResult(string Content, IReadOnlyList<ArtifactReference> ArtifactReferences, string? SessionId = null);

public sealed record AdapterHealth(bool IsHealthy, string Detail);

public interface IAgentAdapter
{
    Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken);

    Task CancelAsync(string runId, CancellationToken cancellationToken);

    Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken);
}
