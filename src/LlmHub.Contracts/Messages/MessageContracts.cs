namespace LlmHub.Contracts.Messages;

public sealed record SendMessageRequest(string ClientMessageId, string Content, bool AwaitResponse, IReadOnlyList<string>? Attachments = null);

public sealed record MessageResponse(
    string MessageId,
    string ChannelId,
    long Sequence,
    string Sender,
    string Recipient,
    string Content,
    string RootMessageId,
    string? ReplyToMessageId,
    string? CausationId,
    int HopCount,
    DateTimeOffset CreatedAt);

public sealed record MessageHistoryResponse(IReadOnlyList<MessageResponse> Messages, long? NextSequence);
