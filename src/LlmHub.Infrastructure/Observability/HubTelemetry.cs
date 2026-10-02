using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LlmHub.Infrastructure.Observability;

public static class HubTelemetry
{
    public static readonly ActivitySource ActivitySource = new("LlmHub");
    public static readonly Meter Meter = new("LlmHub");
    public static readonly Counter<long> MessagesAccepted = Meter.CreateCounter<long>("hub.messages.accepted");
    public static readonly Counter<long> RunsCompleted = Meter.CreateCounter<long>("hub.runs.completed");
    public static readonly Counter<long> WebhookRetries = Meter.CreateCounter<long>("hub.webhooks.retries");
    public static readonly Counter<long> ExpiredLeases = Meter.CreateCounter<long>("hub.leases.expired");

    public static Activity? Start(
        string operation,
        string? channelId = null,
        string? messageId = null,
        string? runId = null,
        string? attemptId = null,
        string? eventId = null,
        string? deliveryId = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("channel_id", channelId);
        activity?.SetTag("message_id", messageId);
        activity?.SetTag("run_id", runId);
        activity?.SetTag("attempt_id", attemptId);
        activity?.SetTag("event_id", eventId);
        activity?.SetTag("delivery_id", deliveryId);
        return activity;
    }
}
