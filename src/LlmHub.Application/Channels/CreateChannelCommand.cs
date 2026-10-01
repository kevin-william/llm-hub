namespace LlmHub.Application.Channels;

public sealed record CreateChannelCommand(
    string OwnerPrincipalId,
    string Destination,
    IReadOnlyList<string>? Participants,
    int? MaxHops);

public sealed record CreateChannelResult(string ChannelId, IReadOnlyList<string> Participants, string Ordering, int MaxHops);

public interface IChannelService
{
    Task<CreateChannelResult> CreateAsync(CreateChannelCommand command, CancellationToken cancellationToken);
}
