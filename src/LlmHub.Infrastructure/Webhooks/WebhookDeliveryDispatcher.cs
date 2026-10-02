using System.Net;
using System.Text;
using System.Text.Json;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Webhooks;

public interface IWebhookDeliveryClient
{
    Task<HttpStatusCode> PostAsync(Uri callback, string payload, WebhookSignature signature, CancellationToken cancellationToken);
}

public interface IWebhookDeliveryDispatcher
{
    Task<int> CreatePendingDeliveriesAsync(CancellationToken cancellationToken);

    Task<int> DeliverPendingAsync(CancellationToken cancellationToken);
}

public sealed class HttpWebhookDeliveryClient(HttpClient httpClient) : IWebhookDeliveryClient
{
    public async Task<HttpStatusCode> PostAsync(Uri callback, string payload, WebhookSignature signature, CancellationToken cancellationToken)
    {
        await CallbackPolicy.ValidateResolvedDestinationAsync(callback, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, callback)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("webhook-id", signature.EventId);
        request.Headers.Add("webhook-timestamp", signature.Timestamp);
        request.Headers.Add("webhook-signature", signature.Value);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }
}

public sealed class WebhookDeliveryDispatcher(
    IDbContextFactory<HubDbContext> contextFactory,
    IWebhookDeliveryClient client,
    IWebhookSecretProtector secretProtector) : IWebhookDeliveryDispatcher
{
    private const int MaximumAttempts = 5;
    private const int MaximumPayloadBytes = 256 * 1024;

    public async Task<int> CreatePendingDeliveriesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var events = await context.OutboxEvents
            .Where(item => item.PublishedAt == null && item.EventName == "message.created")
            .OrderBy(item => item.OccurredAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        foreach (var outboxEvent in events)
        {
            using var eventPayload = JsonDocument.Parse(outboxEvent.Payload);
            var channelId = eventPayload.RootElement.GetProperty("channelId").GetString()
                ?? throw new InvalidOperationException("The message event has no channel id.");
            var subscriptions = await context.Subscriptions
                .Where(item => item.ChannelId == channelId
                    && item.EventName == outboxEvent.EventName
                    && item.IsVerified
                    && item.ExpiresAt > DateTimeOffset.UtcNow)
                .ToArrayAsync(cancellationToken);

            foreach (var subscription in subscriptions)
            {
                var exists = await context.WebhookDeliveries.AnyAsync(
                    item => item.EventId == outboxEvent.Id && item.SubscriptionId == subscription.Id,
                    cancellationToken);
                if (exists)
                {
                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    eventId = outboxEvent.Id,
                    eventName = outboxEvent.EventName,
                    occurredAt = outboxEvent.OccurredAt,
                    cursor = $"{outboxEvent.OccurredAt:O}:{outboxEvent.Id}",
                    data = eventPayload.RootElement,
                });
                var delivery = new WebhookDeliveryRecord
                {
                    Id = $"whd_{Guid.NewGuid():N}",
                    EventId = outboxEvent.Id,
                    SubscriptionId = subscription.Id,
                    Attempt = 1,
                    CallbackUrl = subscription.CallbackUrl,
                    Payload = payload,
                    SignatureTimestamp = outboxEvent.OccurredAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NextAttemptAt = DateTimeOffset.UtcNow,
                };
                if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
                {
                    delivery.State = "dead";
                    delivery.NextAttemptAt = null;
                    delivery.LastError = "payload_too_large";
                }

                context.WebhookDeliveries.Add(delivery);
            }

            outboxEvent.PublishedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        return events.Length;
    }

    public async Task<int> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var deliveries = await context.WebhookDeliveries
            .Where(item => (item.State == "pending" || item.State == "retry_wait")
                && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
            .OrderBy(item => item.NextAttemptAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        foreach (var delivery in deliveries)
        {
            using var activity = HubTelemetry.Start("webhook.deliver", eventId: delivery.EventId, deliveryId: delivery.Id);
            delivery.State = "sending";
            await context.SaveChangesAsync(cancellationToken);
            try
            {
                var subscription = await context.Subscriptions.SingleAsync(item => item.Id == delivery.SubscriptionId, cancellationToken);
                var secret = secretProtector.Unprotect(subscription.EncryptedSecret);
                var signature = WebhookSigner.Create(secret, delivery.EventId, delivery.SignatureTimestamp, delivery.Payload);
                var status = await client.PostAsync(new Uri(delivery.CallbackUrl), delivery.Payload, signature, cancellationToken);
                if ((int)status is >= 200 and < 300)
                {
                    delivery.State = "accepted";
                    delivery.AcceptedAt = DateTimeOffset.UtcNow;
                    delivery.LastError = null;
                    context.AuditEvents.Add(new AuditRecord
                    {
                        Id = $"aud_{Guid.NewGuid():N}", EventName = "webhook.accepted", EventId = delivery.EventId,
                        DeliveryId = delivery.Id, OccurredAt = DateTimeOffset.UtcNow,
                    });
                }
                else if (status == HttpStatusCode.Gone)
                {
                    delivery.State = "dead";
                    delivery.LastError = "subscription_gone";
                    subscription.IsVerified = false;
                    subscription.ExpiresAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    ScheduleRetry(delivery, $"http_{(int)status}");
                }
            }
            catch (HttpRequestException exception)
            {
                ScheduleRetry(delivery, exception.GetType().Name);
            }
            catch (UnsafeWebhookDestinationException exception)
            {
                delivery.State = "dead";
                delivery.LastError = exception.Message;
            }

        }

        await context.SaveChangesAsync(cancellationToken);
        return deliveries.Length;
    }

    private static void ScheduleRetry(WebhookDeliveryRecord delivery, string error)
    {
        HubTelemetry.WebhookRetries.Add(1);
        delivery.LastError = error;
        if (delivery.Attempt >= MaximumAttempts)
        {
            delivery.State = "dead";
            delivery.NextAttemptAt = null;
            return;
        }

        delivery.Attempt++;
        delivery.State = "retry_wait";
        delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, delivery.Attempt));
    }
}
