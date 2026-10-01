using LlmHub.Domain.Common;

namespace LlmHub.Domain.Channels;

public enum ChannelOrdering
{
    Serial,
    Parallel,
}

public sealed class Channel
{
    private readonly HashSet<PrincipalId> _participants;
    private long _lastSequence;
    private RunId? _activeRunId;

    public Channel(ChannelId id, IEnumerable<PrincipalId> participants, ChannelOrdering ordering = ChannelOrdering.Serial, int maxHops = 8)
    {
        if (maxHops < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHops), "The hop limit must be positive.");
        }

        Id = id;
        Ordering = ordering;
        MaxHops = maxHops;
        _participants = participants.ToHashSet();

        if (_participants.Count < 2)
        {
            throw new DomainException("A channel requires at least two participants.");
        }
    }

    public ChannelId Id { get; }

    public ChannelOrdering Ordering { get; }

    public int MaxHops { get; }

    public IReadOnlySet<PrincipalId> Participants => _participants;

    public long LastSequence => _lastSequence;

    public RunId? ActiveRunId => _activeRunId;

    public long ReserveNextSequence()
    {
        _lastSequence++;
        return _lastSequence;
    }

    public void StartRun(RunId runId)
    {
        if (Ordering == ChannelOrdering.Serial && _activeRunId is not null)
        {
            throw new DomainException("A serial channel already has an active run.");
        }

        _activeRunId = runId;
    }

    public void FinishRun(RunId runId)
    {
        if (Ordering == ChannelOrdering.Serial && _activeRunId != runId)
        {
            throw new DomainException("Only the active run can finish a serial channel.");
        }

        _activeRunId = null;
    }

    public void EnsureHopCount(int hopCount)
    {
        if (hopCount < 0 || hopCount > MaxHops)
        {
            throw new DomainException("The message hop count is outside the channel limit.");
        }
    }
}
