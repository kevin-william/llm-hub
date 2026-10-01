using LlmHub.Domain.Common;

namespace LlmHub.Domain.Runs;

public enum RunState
{
    Accepted,
    Queued,
    Leased,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    RetryWait,
    DeadLetter,
}

public enum FailureKind
{
    Transient,
    Permanent,
    Policy,
    Cancelled,
}

public sealed class Run
{
    public Run(RunId id, ChannelId channelId, MessageId inputMessageId, DateTimeOffset acceptedAt)
    {
        Id = id;
        ChannelId = channelId;
        InputMessageId = inputMessageId;
        AcceptedAt = acceptedAt;
        State = RunState.Accepted;
    }

    public RunId Id { get; }

    public ChannelId ChannelId { get; }

    public MessageId InputMessageId { get; }

    public DateTimeOffset AcceptedAt { get; }

    public RunState State { get; private set; }

    public FailureKind? FailureCategory { get; private set; }

    public void Queue() => Transition(RunState.Queued, RunState.Accepted, RunState.RetryWait);

    public void Lease() => Transition(RunState.Leased, RunState.Queued);

    public void Start() => Transition(RunState.Running, RunState.Leased);

    public void Succeed() => Transition(RunState.Succeeded, RunState.Running);

    public void Cancel() => Transition(RunState.Cancelled, RunState.Accepted, RunState.Queued, RunState.Leased, RunState.Running, RunState.RetryWait);

    public void TimeOut() => Transition(RunState.TimedOut, RunState.Leased, RunState.Running);

    public void Fail(FailureKind failureKind)
    {
        if (State is not (RunState.Leased or RunState.Running))
        {
            throw new DomainException($"A run cannot fail from state '{State}'.");
        }

        FailureCategory = failureKind;
        State = failureKind switch
        {
            FailureKind.Transient => RunState.RetryWait,
            FailureKind.Cancelled => RunState.Cancelled,
            _ => RunState.Failed,
        };
    }

    public void SendToDeadLetter() => Transition(RunState.DeadLetter, RunState.RetryWait);

    private void Transition(RunState target, params RunState[] allowedFrom)
    {
        if (!allowedFrom.Contains(State))
        {
            throw new DomainException($"A run cannot transition from '{State}' to '{target}'.");
        }

        State = target;
    }
}
