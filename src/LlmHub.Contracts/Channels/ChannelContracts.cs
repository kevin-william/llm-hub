namespace LlmHub.Contracts.Channels;

public sealed record OpenChannelRequest(string Destination, IReadOnlyList<string>? Participants = null, int? MaxHops = null);

public sealed record ChannelResponse(string ChannelId, IReadOnlyList<string> Participants, string Ordering, int MaxHops);
