namespace LlmHub.Application.Channels;

public sealed record CreateChannelCommand(
    string OwnerPrincipalId,
    string Destination,
    IReadOnlyList<string>? Participants,
    int? MaxHops,
    IReadOnlyList<string>? RequiredCapabilities = null,
    string TenantId = "tenant:development");

public sealed record CreateChannelResult(string ChannelId, string Destination, IReadOnlyList<string> Participants, string Ordering, int MaxHops);

public interface IChannelService
{
    Task<CreateChannelResult> CreateAsync(CreateChannelCommand command, CancellationToken cancellationToken);
}
