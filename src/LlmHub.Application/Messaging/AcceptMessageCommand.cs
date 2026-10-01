namespace LlmHub.Application.Messaging;

public sealed record AcceptMessageCommand(
    string ChannelId,
    string PrincipalId,
    string Sender,
    string Recipient,
    string ClientMessageId,
    string Content,
    bool AwaitResponse,
    IReadOnlyList<string>? Attachments = null);

public sealed record AcceptMessageResult(string MessageId, string RunId, long Sequence, bool IsReplay);

public interface IMessageAcceptanceService
{
    Task<AcceptMessageResult> AcceptAsync(AcceptMessageCommand command, CancellationToken cancellationToken);
}
