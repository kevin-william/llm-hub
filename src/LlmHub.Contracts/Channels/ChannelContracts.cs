namespace LlmHub.Contracts.Channels;

public sealed record OpenChannelRequest(
    string Destination,
    IReadOnlyList<string>? Participants = null,
    int? MaxHops = null,
    IReadOnlyList<string>? RequiredCapabilities = null);

public sealed record ChannelResponse(string ChannelId, string Destination, IReadOnlyList<string> Participants, string Ordering, int MaxHops);
