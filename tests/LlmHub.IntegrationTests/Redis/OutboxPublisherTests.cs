using LlmHub.Contracts.Queues;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Redis;

public sealed class OutboxPublisherTests
{
    [Fact]
    public async Task PublishedEventIsMarkedOnlyAfterQueueAcceptsIt()
    {
        var factory = CreateFactory();
        await SeedPendingEventAsync(factory);
        var queue = new RecordingQueue();
        var publisher = new OutboxPublisher(factory, queue);

        var count = await publisher.PublishPendingAsync(CancellationToken.None);
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var outboxEvent = await context.OutboxEvents.SingleAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Single(queue.Published);
        Assert.Equal("evt_1", queue.Published[0].EventId);
        Assert.NotNull(outboxEvent.PublishedAt);
    }

    [Fact]
    public async Task FailedPublicationLeavesEventPendingForReplay()
    {
        var factory = CreateFactory();
        await SeedPendingEventAsync(factory);
        var publisher = new OutboxPublisher(factory, new FailingQueue());

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishPendingAsync(CancellationToken.None));
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Null((await context.OutboxEvents.SingleAsync(CancellationToken.None)).PublishedAt);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase($"outbox-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task SeedPendingEventAsync(TestDbContextFactory factory)
    {
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        context.Runs.Add(new RunRecord
        {
            Id = "run_1",
            ChannelId = "chn_1",
            InputMessageId = "msg_1",
            AcceptedAt = DateTimeOffset.UtcNow,
        });
        context.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = "evt_1",
            EventName = "run.queued",
            AggregateId = "run_1",
            Payload = "{\"channelId\":\"chn_1\"}",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class RecordingQueue : IRunQueue
    {
        public List<QueuedRun> Published { get; } = [];

        public Task PublishAsync(QueuedRun run, CancellationToken cancellationToken)
        {
            Published.Add(run);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingQueue : IRunQueue
    {
        public Task PublishAsync(QueuedRun run, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("Redis is unavailable."));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
