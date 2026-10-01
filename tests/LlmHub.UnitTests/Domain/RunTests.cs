using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;

namespace LlmHub.UnitTests.Domain;

public sealed class RunTests
{
    [Fact]
    public void RunFollowsSuccessfulLifecycle()
    {
        var run = NewRun();

        run.Queue();
        run.Lease();
        run.Start();
        run.Succeed();

        Assert.Equal(RunState.Succeeded, run.State);
    }

    [Fact]
    public void RunRejectsInvalidTransition()
    {
        var run = NewRun();

        Assert.Throws<DomainException>(run.Succeed);
    }

    [Fact]
    public void TransientFailureCanBeRetriedOrDeadLettered()
    {
        var run = NewRun();
        run.Queue();
        run.Lease();
        run.Fail(FailureKind.Transient);

        Assert.Equal(RunState.RetryWait, run.State);
        run.Queue();
        run.Lease();
        run.Fail(FailureKind.Transient);
        run.SendToDeadLetter();

        Assert.Equal(RunState.DeadLetter, run.State);
    }

    private static Run NewRun() => new(
        RunId.Create("run_1"),
        ChannelId.Create("chn_1"),
        MessageId.Create("msg_1"),
        DateTimeOffset.UtcNow);
}
