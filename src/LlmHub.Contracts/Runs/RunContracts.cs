namespace LlmHub.Contracts.Runs;

public sealed record RunResponse(string RunId, string ChannelId, string InputMessageId, string State, DateTimeOffset AcceptedAt);
