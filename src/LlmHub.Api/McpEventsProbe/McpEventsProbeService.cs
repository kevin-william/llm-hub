using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LlmHub.Api.McpEventsProbe;

public interface IMcpEventsProbeCallbackClient
{
    Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken);

    Task<HttpStatusCode> DeliverAsync(Uri callback, string payload, string eventId, string timestamp, string signature, CancellationToken cancellationToken);
}

public interface IMcpEventsProbeService
{
    IReadOnlyList<McpEventDefinition> ListEvents();

    Task<McpEventSubscription> SubscribeAsync(string callbackUrl, string secret, int durationSeconds, CancellationToken cancellationToken);

    bool Unsubscribe(string subscriptionId);

    Task<McpEventDispatchResult> PublishAsync(string eventName, JsonElement data, string? eventId, CancellationToken cancellationToken);
}

public sealed record McpEventDefinition(string Name, string Description);

public sealed record McpEventSubscription(string SubscriptionId, DateTimeOffset ExpiresAt, bool IsVerified);

public sealed record McpEventDispatchResult(string EventId, int Accepted, int Failed);

public sealed class HttpMcpEventsProbeCallbackClient(HttpClient httpClient) : IMcpEventsProbeCallbackClient
{
    public async Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, callback)
        {
            Content = JsonContent.Create(new { type = "challenge", challenge }),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<HttpStatusCode> DeliverAsync(Uri callback, string payload, string eventId, string timestamp, string signature, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, callback)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Mcp-Event-Id", eventId);
        request.Headers.Add("X-Mcp-Event-Timestamp", timestamp);
        request.Headers.Add("X-Mcp-Signature", signature);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }
}

public sealed class McpEventsProbeService(IMcpEventsProbeCallbackClient callbackClient, TimeProvider? timeProvider = null) : IMcpEventsProbeService
{
    private const string MessageCreated = "message.created";
    private readonly ConcurrentDictionary<string, Subscription> subscriptions = new();
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<McpEventDefinition> ListEvents() =>
    [new(MessageCreated, "A message was created in the Hub.")];

    public async Task<McpEventSubscription> SubscribeAsync(string callbackUrl, string secret, int durationSeconds, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("callbackUrl must be an absolute HTTPS URL.", nameof(callbackUrl));
        }

        if (string.IsNullOrWhiteSpace(secret) || durationSeconds is < 1 or > 86_400)
        {
            throw new ArgumentException("secret and a duration between 1 and 86400 seconds are required.");
        }

        var existing = subscriptions.Values.FirstOrDefault(item => item.Callback == callback && item.Secret == secret && item.ExpiresAt > timeProvider.GetUtcNow());
        if (existing is not null)
        {
            return new McpEventSubscription(existing.Id, existing.ExpiresAt, true);
        }

        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        if (!await callbackClient.VerifyAsync(callback, challenge, cancellationToken))
        {
            throw new InvalidOperationException("The callback did not accept the subscription challenge.");
        }

        var subscription = new Subscription(
            $"mcp_sub_{Guid.NewGuid():N}",
            callback,
            secret,
            timeProvider.GetUtcNow().AddSeconds(durationSeconds));
        subscriptions[subscription.Id] = subscription;
        return new McpEventSubscription(subscription.Id, subscription.ExpiresAt, true);
    }

    public bool Unsubscribe(string subscriptionId) => subscriptions.TryRemove(subscriptionId, out _);

    public async Task<McpEventDispatchResult> PublishAsync(string eventName, JsonElement data, string? eventId, CancellationToken cancellationToken)
    {
        if (eventName != MessageCreated)
        {
            throw new ArgumentException("Only message.created is available in the probe.", nameof(eventName));
        }

        var id = string.IsNullOrWhiteSpace(eventId) ? $"mcp_evt_{Guid.NewGuid():N}" : eventId;
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new { eventId = id, eventName, timestamp, data });
        var accepted = 0;
        var failed = 0;
        foreach (var subscription in subscriptions.Values.Where(item => item.ExpiresAt > timeProvider.GetUtcNow()))
        {
            var signature = CreateSignature(subscription.Secret, timestamp, payload);
            var status = await callbackClient.DeliverAsync(subscription.Callback, payload, id, timestamp, signature, cancellationToken);
            if ((int)status is >= 200 and < 300)
            {
                accepted++;
            }
            else
            {
                failed++;
            }
        }

        return new McpEventDispatchResult(id, accepted, failed);
    }

    public static string CreateSignature(string secret, string timestamp, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var key = Encoding.UTF8.GetBytes(secret);
        return $"v1,{Convert.ToHexString(HMACSHA256.HashData(key, bytes)).ToLowerInvariant()}";
    }

    public static bool VerifySignature(string secret, string timestamp, string payload, string signature)
    {
        var expected = Encoding.UTF8.GetBytes(CreateSignature(secret, timestamp, payload));
        var received = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private sealed record Subscription(string Id, Uri Callback, string Secret, DateTimeOffset ExpiresAt);
}
