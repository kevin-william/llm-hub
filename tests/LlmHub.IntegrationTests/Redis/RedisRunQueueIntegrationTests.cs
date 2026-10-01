using LlmHub.Contracts.Queues;
using LlmHub.Infrastructure.Redis;
using StackExchange.Redis;

namespace LlmHub.IntegrationTests.Redis;

public sealed class RedisRunQueueIntegrationTests
{
    [Fact]
    public async Task PublishesRunToTheAdapterSpecificRedisStream()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
        var queue = new RedisRunQueue(connection);
        var adapter = $"test-{Guid.NewGuid():N}";
        var run = new QueuedRun("evt_redis", "run_redis", adapter, "chn_redis", DateTimeOffset.UtcNow);

        await queue.PublishAsync(run, CancellationToken.None);

        var entries = await connection.GetDatabase().StreamRangeAsync(RedisRunQueue.StreamName(adapter));
        var entry = Assert.Single(entries);
        Assert.Contains(entry.Values, value => value.Name == "event_id" && value.Value == run.EventId);
        Assert.Contains(entry.Values, value => value.Name == "run_id" && value.Value == run.RunId);
    }

    [Fact]
    public async Task ConsumerGroupReadsPendingDeliveryAndAcknowledgesIt()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
        var queue = new RedisRunQueue(connection);
        var adapter = $"group-{Guid.NewGuid():N}";
        var group = "workers";
        var run = new QueuedRun("evt_group", "run_group", adapter, "chn_group", DateTimeOffset.UtcNow);
        await queue.PublishAsync(run, CancellationToken.None);
        await queue.EnsureConsumerGroupAsync(adapter, group, CancellationToken.None);

        var first = await queue.ReadAsync(adapter, group, "consumer-a", 1, recoverPending: false, CancellationToken.None);
        var pending = await queue.ReadAsync(adapter, group, "consumer-a", 1, recoverPending: true, CancellationToken.None);
        await queue.AcknowledgeAsync(adapter, group, first.Single().EntryId, CancellationToken.None);

        Assert.Equal(run.RunId, first.Single().Run.RunId);
        Assert.Equal(first.Single().EntryId, pending.Single().EntryId);
    }
}
