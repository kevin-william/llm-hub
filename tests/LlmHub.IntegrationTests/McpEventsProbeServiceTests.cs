using System.Net;
using System.Text.Json;
using LlmHub.Api.McpEventsProbe;

namespace LlmHub.IntegrationTests;

public sealed class McpEventsProbeServiceTests
{
    [Fact]
    public async Task DuplicateSubscriptionHasOneDeliveryAndHmacCanBeVerified()
    {
        var callback = new FakeCallbackClient();
        var service = new McpEventsProbeService(callback);

        var first = await service.SubscribeAsync("https://callback.example.test/events", "secret", 300, CancellationToken.None);
        var duplicate = await service.SubscribeAsync("https://callback.example.test/events", "secret", 300, CancellationToken.None);
        using var document = JsonDocument.Parse("{\"messageId\":\"msg_1\"}");
        var published = await service.PublishAsync("message.created", document.RootElement, "mcp_evt_1", CancellationToken.None);

        Assert.Equal(first.SubscriptionId, duplicate.SubscriptionId);
        Assert.Equal(1, published.Accepted);
        var delivery = Assert.Single(callback.Deliveries);
        Assert.True(McpEventsProbeService.VerifySignature("secret", delivery.Timestamp, delivery.Payload, delivery.Signature));
        Assert.False(McpEventsProbeService.VerifySignature("secret", delivery.Timestamp, "{}", delivery.Signature));
        Assert.True(service.Unsubscribe(first.SubscriptionId));
        Assert.False(service.Unsubscribe(first.SubscriptionId));
    }

    [Fact]
    public async Task RejectedChallengeDoesNotCreateSubscription()
    {
        var service = new McpEventsProbeService(new FakeCallbackClient { ChallengeAccepted = false });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubscribeAsync(
            "https://callback.example.test/events", "secret", 300, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredSubscriptionDoesNotReceivePublication()
    {
        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-10-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var callback = new FakeCallbackClient();
        var service = new McpEventsProbeService(callback, clock);
        await service.SubscribeAsync("https://callback.example.test/events", "secret", 1, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse("{\"messageId\":\"msg_1\"}");

        var published = await service.PublishAsync("message.created", document.RootElement, null, CancellationToken.None);

        Assert.Equal(0, published.Accepted);
        Assert.Empty(callback.Deliveries);
    }

    private sealed class FakeCallbackClient : IMcpEventsProbeCallbackClient
    {
        public bool ChallengeAccepted { get; init; } = true;

        public List<Delivery> Deliveries { get; } = [];

        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(ChallengeAccepted);

        public Task<HttpStatusCode> DeliverAsync(Uri callback, string payload, string eventId, string timestamp, string signature, CancellationToken cancellationToken)
        {
            Deliveries.Add(new Delivery(payload, timestamp, signature));
            return Task.FromResult(HttpStatusCode.NoContent);
        }
    }

    private sealed record Delivery(string Payload, string Timestamp, string Signature);

    private sealed class ProbeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
