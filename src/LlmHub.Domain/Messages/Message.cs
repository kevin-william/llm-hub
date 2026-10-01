using LlmHub.Domain.Common;

namespace LlmHub.Domain.Messages;

public sealed record Message(
    MessageId Id,
    ChannelId ChannelId,
    long Sequence,
    PrincipalId Sender,
    PrincipalId Recipient,
    string Content,
    MessageId RootMessageId,
    MessageId? ReplyToMessageId,
    MessageId? CausationId,
    int HopCount,
    IReadOnlyList<string> Attachments,
    DateTimeOffset CreatedAt)
{
    public static Message Create(
        MessageId id,
        ChannelId channelId,
        long sequence,
        PrincipalId sender,
        PrincipalId recipient,
        string content,
        MessageId rootMessageId,
        MessageId? replyToMessageId,
        MessageId? causationId,
        int hopCount,
        IEnumerable<string>? attachments,
        DateTimeOffset createdAt)
    {
        if (sequence < 1)
        {
            throw new DomainException("A message sequence must be positive.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("Message content is required.");
        }

        if (hopCount < 0)
        {
            throw new DomainException("The hop count cannot be negative.");
        }

        return new Message(
            id,
            channelId,
            sequence,
            sender,
            recipient,
            content,
            rootMessageId,
            replyToMessageId,
            causationId,
            hopCount,
            attachments?.Where(attachment => !string.IsNullOrWhiteSpace(attachment)).ToArray() ?? [],
            createdAt);
    }
}
