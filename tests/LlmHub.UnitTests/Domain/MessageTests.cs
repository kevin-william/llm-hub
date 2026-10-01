using LlmHub.Domain.Common;
using LlmHub.Domain.Messages;

namespace LlmHub.UnitTests.Domain;

public sealed class MessageTests
{
    [Fact]
    public void MessageRetainsCorrelationFieldsAndFiltersBlankAttachments()
    {
        var root = MessageId.Create("msg_1");
        var message = Message.Create(
            MessageId.Create("msg_2"),
            ChannelId.Create("chn_1"),
            2,
            PrincipalId.Create("agent:opencode/default"),
            PrincipalId.Create("principal:chatgpt/user-1"),
            "Resultado",
            root,
            root,
            root,
            1,
            ["artifact_1", ""],
            DateTimeOffset.UtcNow);

        Assert.Equal(root, message.RootMessageId);
        Assert.Equal(root, message.ReplyToMessageId);
        Assert.Equal(root, message.CausationId);
        Assert.Single(message.Attachments);
    }

    [Fact]
    public void MessageRejectsInvalidSequenceAndContent()
    {
        Assert.Throws<DomainException>(() => Message.Create(
            MessageId.Create("msg_1"),
            ChannelId.Create("chn_1"),
            0,
            PrincipalId.Create("sender"),
            PrincipalId.Create("recipient"),
            "content",
            MessageId.Create("msg_1"),
            null,
            null,
            0,
            [],
            DateTimeOffset.UtcNow));
    }
}
