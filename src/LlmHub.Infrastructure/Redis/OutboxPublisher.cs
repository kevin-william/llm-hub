using System.Text.Json;
using LlmHub.Contracts.Queues;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Redis;

public interface IOutboxPublisher
{
    Task<int> PublishPendingAsync(CancellationToken cancellationToken);
}

public sealed class OutboxPublisher(IDbContextFactory<HubDbContext> contextFactory, IRunQueue queue) : IOutboxPublisher
{
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await context.OutboxEvents
            .Where(outboxEvent => outboxEvent.PublishedAt == null && outboxEvent.EventName == "run.queued")
            .OrderBy(outboxEvent => outboxEvent.OccurredAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        foreach (var outboxEvent in pending)
        {
            var run = await context.Runs.SingleAsync(item => item.Id == outboxEvent.AggregateId, cancellationToken);
            using var document = JsonDocument.Parse(outboxEvent.Payload);
            var payload = document.RootElement;
            var channelId = payload.GetProperty("channelId").GetString()
                ?? throw new InvalidOperationException("The queued run payload has no channel id.");

            await queue.PublishAsync(
                new QueuedRun(outboxEvent.Id, run.Id, run.Adapter, channelId, outboxEvent.OccurredAt),
                cancellationToken);
            outboxEvent.PublishedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        return pending.Length;
    }
}
