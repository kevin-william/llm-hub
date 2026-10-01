using System.Net;
using System.Text.Json;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Webhooks;

public sealed class WebhookDeliveryDispatcherTests
{
    [Theory]
    [InlineData("http://callback.example.test/events")]
    [InlineData("https://localhost/events")]
    [InlineData("https://127.0.0.1/events")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public void UnsafeCallbackAddressesAreRejected(string callbackUrl)
    {
        Assert.Throws<UnsafeWebhookDestinationException>(() => CallbackPolicy.ValidateUri(new Uri(callbackUrl)));
    }

    [Fact]
    public async Task MatchingEventIsRetriedWithTheSameEventIdThenAccepted()
    {
        var factory = CreateFactory();
        await SeedAsync(factory);
        var client = new FakeWebhookClient(HttpStatusCode.InternalServerError, HttpStatusCode.NoContent);
        var dispatcher = new WebhookDeliveryDispatcher(factory, client, new TestSecretProtector());

        Assert.Equal(1, await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None));

        Assert.Equal(1, await dispatcher.DeliverPendingAsync(CancellationToken.None));
        await using (var beforeRetry = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var delivery = await beforeRetry.WebhookDeliveries.SingleAsync(CancellationToken.None);
            Assert.Equal("retry_wait", delivery.State);
            Assert.Equal(2, delivery.Attempt);
            using var payload = JsonDocument.Parse(delivery.Payload);
            Assert.Equal(delivery.EventId, payload.RootElement.GetProperty("eventId").GetString());
            Assert.True(payload.RootElement.TryGetProperty("cursor", out _));
            delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await beforeRetry.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(1, await dispatcher.DeliverPendingAsync(CancellationToken.None));
        await using var afterSuccess = await factory.CreateDbContextAsync(CancellationToken.None);
        var accepted = await afterSuccess.WebhookDeliveries.SingleAsync(CancellationToken.None);
        Assert.Equal("accepted", accepted.State);
        Assert.NotNull(accepted.AcceptedAt);
        Assert.Equal(2, client.Payloads.Count);
        Assert.Equal(client.Payloads[0], client.Payloads[1]);
        Assert.Equal(client.Signatures[0], client.Signatures[1]);
        Assert.True(WebhookSigner.Verify("test", client.Signatures[0].EventId, client.Signatures[0].Timestamp, client.Payloads[0], client.Signatures[0].Value));
    }

    [Fact]
    public async Task GoneCallbackDisablesSubscriptionAndMarksDeliveryDead()
    {
        var factory = CreateFactory();
        await SeedAsync(factory);
        var dispatcher = new WebhookDeliveryDispatcher(factory, new FakeWebhookClient(HttpStatusCode.Gone), new TestSecretProtector());

        await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None);
        await dispatcher.DeliverPendingAsync(CancellationToken.None);

        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var delivery = await context.WebhookDeliveries.SingleAsync(CancellationToken.None);
        var subscription = await context.Subscriptions.SingleAsync(item => item.Id == delivery.SubscriptionId, CancellationToken.None);
        Assert.Equal("dead", delivery.State);
        Assert.Equal("subscription_gone", delivery.LastError);
        Assert.False(subscription.IsVerified);
    }

    [Fact]
    public async Task PayloadOver256KiBIsTrackedButNeverSent()
    {
        var factory = CreateFactory();
        await SeedAsync(factory);
        await using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var eventRecord = await context.OutboxEvents.SingleAsync(CancellationToken.None);
            eventRecord.Payload = JsonSerializer.Serialize(new
            {
                messageId = "msg_1",
                channelId = "chn_1",
                content = new string('x', 300 * 1024),
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var client = new FakeWebhookClient(HttpStatusCode.NoContent);
        var dispatcher = new WebhookDeliveryDispatcher(factory, client, new TestSecretProtector());

        await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None);
        Assert.Equal(0, await dispatcher.DeliverPendingAsync(CancellationToken.None));
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);
        var delivery = await verify.WebhookDeliveries.SingleAsync(CancellationToken.None);
        Assert.Equal("dead", delivery.State);
        Assert.Equal("payload_too_large", delivery.LastError);
        Assert.Empty(client.Payloads);
    }

    private static async Task SeedAsync(TestDbContextFactory factory)
    {
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        context.Subscriptions.AddRange(
            new SubscriptionRecord
            {
                Id = "sub_match",
                PrincipalId = "principal:chatgpt/user-1",
                ChannelId = "chn_1",
                EventName = "message.created",
                CallbackUrl = "https://callback.example.test/events",
                EncryptedSecret = "test",
                IsVerified = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            },
            new SubscriptionRecord
            {
                Id = "sub_other_event",
                PrincipalId = "principal:chatgpt/user-1",
                ChannelId = "chn_1",
                EventName = "run.queued",
                CallbackUrl = "https://callback.example.test/other",
                EncryptedSecret = "test",
                IsVerified = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });
        context.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = "evt_1",
            EventName = "message.created",
            AggregateId = "msg_1",
            Payload = "{\"messageId\":\"msg_1\",\"channelId\":\"chn_1\",\"runId\":\"run_1\"}",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase($"llm-hub-webhooks-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private sealed class FakeWebhookClient(params HttpStatusCode[] statuses) : IWebhookDeliveryClient
    {
        private readonly Queue<HttpStatusCode> statuses = new(statuses);

        public List<string> Payloads { get; } = [];

        public List<WebhookSignature> Signatures { get; } = [];

        public Task<HttpStatusCode> PostAsync(Uri callback, string payload, WebhookSignature signature, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            Signatures.Add(signature);
            return Task.FromResult(statuses.Dequeue());
        }
    }

    private sealed class TestSecretProtector : IWebhookSecretProtector
    {
        public string Protect(string secret) => secret;

        public string Unprotect(string protectedSecret) => protectedSecret;
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
