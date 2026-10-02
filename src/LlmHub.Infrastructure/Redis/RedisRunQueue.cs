using LlmHub.Contracts.Queues;
using StackExchange.Redis;

namespace LlmHub.Infrastructure.Redis;

#pragma warning disable CA1711 // Queue is the domain term used by the public transport contract.
public sealed class RedisRunQueue(IConnectionMultiplexer connection) : IRunQueue, IRunQueueConsumer
{
    public async Task PublishAsync(QueuedRun run, CancellationToken cancellationToken)
    {
        var database = connection.GetDatabase();
        var entries = new NameValueEntry[]
        {
            new("event_id", run.EventId),
            new("run_id", run.RunId),
            new("channel_id", run.ChannelId),
            new("queued_at", run.QueuedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        };

        await database.StreamAddAsync(StreamName(run.Adapter), entries).WaitAsync(cancellationToken);
    }

    public static RedisKey StreamName(string adapter) => $"hub:runs:{adapter}";

    public async Task EnsureConsumerGroupAsync(string adapter, string groupName, CancellationToken cancellationToken)
    {
        try
        {
            await connection.GetDatabase().StreamCreateConsumerGroupAsync(StreamName(adapter), groupName, "0-0", createStream: true).WaitAsync(cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // The group already exists and can be reused after a worker restart.
        }
    }

    public async Task<IReadOnlyList<QueuedRunDelivery>> ReadAsync(string adapter, string groupName, string consumerName, int count, bool recoverPending, CancellationToken cancellationToken)
    {
        var position = recoverPending ? "0" : ">";
        var entries = await connection.GetDatabase()
            .StreamReadGroupAsync(StreamName(adapter), groupName, consumerName, position, count: Math.Clamp(count, 1, 100))
            .WaitAsync(cancellationToken);
        return entries.Select(entry => new QueuedRunDelivery(
            entry.Id!,
            new QueuedRun(
                Value(entry, "event_id"),
                Value(entry, "run_id"),
                adapter,
                Value(entry, "channel_id"),
                DateTimeOffset.Parse(Value(entry, "queued_at"), System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
    }

    public Task AcknowledgeAsync(string adapter, string groupName, string entryId, CancellationToken cancellationToken)
        => connection.GetDatabase().StreamAcknowledgeAsync(StreamName(adapter), groupName, entryId).WaitAsync(cancellationToken);

    private static string Value(StreamEntry entry, string name)
        => entry.Values.Single(value => value.Name == name).Value!;
}
#pragma warning restore CA1711
