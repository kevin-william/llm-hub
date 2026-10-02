using LlmHub.Contracts.Queues;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Redis;
using LlmHub.Infrastructure.Webhooks;
using LlmHub.Infrastructure.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LlmHub.Workers;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
var publishRuns = builder.Configuration.GetValue<bool>("Workers:EnableOutboxPublisher");
var dispatchWebhooks = builder.Configuration.GetValue<bool>("Workers:EnableWebhookDispatcher");
var runRetention = builder.Configuration.GetValue<bool>("Workers:EnableRetention");
if (publishRuns || dispatchWebhooks || runRetention)
{
    var postgres = builder.Configuration.GetConnectionString("Hub")
        ?? throw new InvalidOperationException("ConnectionStrings:Hub is required when background delivery is enabled.");

    builder.Services.AddHubPersistence(
        postgres,
        builder.Configuration["Webhook:SecretEncryptionKey"],
        builder.Configuration["Webhook:PreviousSecretEncryptionKeys"]);
    builder.Services.AddSingleton(new RetentionOptions(
        builder.Configuration.GetValue<int?>("Retention:AuditDays") ?? 90,
        builder.Configuration.GetValue<int?>("Retention:WebhookDeliveryDays") ?? 30));
    if (publishRuns)
    {
        var redis = builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required when the outbox publisher is enabled.");
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        builder.Services.AddSingleton<IRunQueue, RedisRunQueue>();
        builder.Services.AddSingleton<IOutboxPublisher, OutboxPublisher>();
        builder.Services.AddHostedService<OutboxPublisherWorker>();
    }

    if (dispatchWebhooks)
    {
        builder.Services.AddHostedService<WebhookDispatcherWorker>();
    }

    if (runRetention)
    {
        builder.Services.AddHostedService<RetentionWorker>();
    }
}

var host = builder.Build();
host.Run();
