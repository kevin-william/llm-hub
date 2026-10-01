namespace LlmHub.Contracts.Queues;

public sealed record QueuedRun(string EventId, string RunId, string Adapter, string ChannelId, DateTimeOffset QueuedAt);

public sealed record QueuedRunDelivery(string EntryId, QueuedRun Run);

#pragma warning disable CA1711 // Queue is the domain term used by the public transport contract.
public interface IRunQueue
{
    Task PublishAsync(QueuedRun run, CancellationToken cancellationToken);
}

public interface IRunQueueConsumer
{
    Task EnsureConsumerGroupAsync(string adapter, string groupName, CancellationToken cancellationToken);

    Task<IReadOnlyList<QueuedRunDelivery>> ReadAsync(string adapter, string groupName, string consumerName, int count, bool recoverPending, CancellationToken cancellationToken);

    Task AcknowledgeAsync(string adapter, string groupName, string entryId, CancellationToken cancellationToken);
}
#pragma warning restore CA1711
