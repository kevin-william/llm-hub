using LlmHub.Contracts.Artifacts;

namespace LlmHub.Contracts.Workers;

public sealed record RegisterWorkerRequest(string WorkerId, string Adapter, IReadOnlyList<string> Capabilities, string Version, int MaxConcurrency);

public sealed record ClaimRequest(int? WaitSeconds = null);

public sealed record ClaimedRunResponse(string RunId, string ChannelId, string InputMessageId, string Content, string LeaseToken, DateTimeOffset LeaseExpiresAt, DateTimeOffset? Deadline, string? SessionId = null);

public sealed record HeartbeatRequest(string LeaseToken);

public sealed record HeartbeatResponse(DateTimeOffset LeaseExpiresAt, bool CancelRequested);

public sealed record CompleteRunRequest(string LeaseToken, string? Content = null, string? SessionId = null, IReadOnlyList<ArtifactReference>? Artifacts = null);

public sealed record FailRunRequest(string LeaseToken, string FailureKind);
