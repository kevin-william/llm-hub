using LlmHub.Domain.Channels;
using LlmHub.Domain.Common;

namespace LlmHub.UnitTests.Domain;

public sealed class ChannelTests
{
    [Fact]
    public void SerialChannelRejectsSecondActiveRunAndHopCountAboveLimit()
    {
        var channel = new Channel(
            ChannelId.Create("chn_1"),
            [PrincipalId.Create("principal:chatgpt/user-1"), PrincipalId.Create("agent:opencode/default")]);

        channel.StartRun(RunId.Create("run_1"));

        Assert.Throws<DomainException>(() => channel.StartRun(RunId.Create("run_2")));
        Assert.Throws<DomainException>(() => channel.EnsureHopCount(9));
    }

    [Fact]
    public void SerialChannelAssignsMonotonicSequencesAndReleasesActiveRun()
    {
        var channel = new Channel(
            ChannelId.Create("chn_1"),
            [PrincipalId.Create("principal:chatgpt/user-1"), PrincipalId.Create("agent:opencode/default")]);
        var runId = RunId.Create("run_1");

        var first = channel.ReserveNextSequence();
        var second = channel.ReserveNextSequence();
        channel.StartRun(runId);
        channel.FinishRun(runId);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Null(channel.ActiveRunId);
    }
}
