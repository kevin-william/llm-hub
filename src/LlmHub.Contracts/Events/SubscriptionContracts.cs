namespace LlmHub.Contracts.Events;

public sealed record CreateSubscriptionRequest(string ChannelId, string EventName, string CallbackUrl, string Secret, int DurationSeconds);

public sealed record SubscriptionResponse(string SubscriptionId, string ChannelId, string EventName, DateTimeOffset ExpiresAt, bool IsVerified);
