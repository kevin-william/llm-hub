using LlmHub.Infrastructure.Maintenance;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Maintenance;

public sealed class RetentionServiceTests
{
    [Fact]
    public async Task PurgesOnlyExpiredAuditAndTerminalDeliveries()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"retention-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.AuditEvents.AddRange(
                new AuditRecord { Id = "aud_old", EventName = "old", OccurredAt = DateTimeOffset.UtcNow.AddDays(-31) },
                new AuditRecord { Id = "aud_new", EventName = "new", OccurredAt = DateTimeOffset.UtcNow });
            seed.WebhookDeliveries.AddRange(
                new WebhookDeliveryRecord { Id = "whd_old", EventId = "evt_old", SubscriptionId = "sub_1", State = "accepted", AcceptedAt = DateTimeOffset.UtcNow.AddDays(-31), CallbackUrl = "https://example.test", SignatureTimestamp = "1" },
                new WebhookDeliveryRecord { Id = "whd_pending", EventId = "evt_pending", SubscriptionId = "sub_1", State = "pending", NextAttemptAt = DateTimeOffset.UtcNow.AddDays(-31), CallbackUrl = "https://example.test", SignatureTimestamp = "1" });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var result = await new RetentionService(factory, new RetentionOptions(AuditDays: 30, WebhookDeliveryDays: 30)).PurgeAsync(CancellationToken.None);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Equal(new(1, 1), result);
        Assert.Single(verify.AuditEvents);
        Assert.Single(verify.WebhookDeliveries);
        Assert.Equal("pending", (await verify.WebhookDeliveries.SingleAsync()).State);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
