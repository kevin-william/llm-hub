using LlmHub.Application.Channels;
using LlmHub.Application.Events;
using LlmHub.Application.Messaging;
using LlmHub.Application.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LlmHub.Infrastructure.Webhooks;
using LlmHub.Infrastructure.Artifacts;
using LlmHub.Infrastructure.Maintenance;
using LlmHub.Application.Maintenance;

namespace LlmHub.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddHubPersistence(
        this IServiceCollection services,
        string connectionString,
        string? webhookSecretEncryptionKey = null,
        string? webhookPreviousSecretEncryptionKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<HubDbContext>(options => options.UseNpgsql(connectionString));
        AddWebhookSecretProtection(services, webhookSecretEncryptionKey, webhookPreviousSecretEncryptionKeys);
        services.AddScoped<IChannelService, PostgresChannelService>();
        services.AddScoped<IMessageAcceptanceService, PostgresMessageAcceptanceService>();
        services.AddScoped<IWorkerGateway, PostgresWorkerGateway>();
        services.AddScoped<IRunRecoveryService, RunRecoveryService>();
        services.AddScoped<ISubscriptionService, PostgresSubscriptionService>();
        services.AddScoped<IHubRetentionService, RetentionService>();
        services.AddSingleton<IWebhookDeliveryClient>(_ => new HttpWebhookDeliveryClient(new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        }));
        services.AddSingleton<IWebhookVerificationClient>(_ => new HttpWebhookVerificationClient(new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        }));
        services.AddScoped<IWebhookDeliveryDispatcher, WebhookDeliveryDispatcher>();
        return services;
    }

    public static IServiceCollection AddHubInMemoryPersistence(
        this IServiceCollection services,
        string databaseName,
        string? webhookSecretEncryptionKey = null,
        string? webhookPreviousSecretEncryptionKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        services.AddDbContextFactory<HubDbContext>(options => options.UseInMemoryDatabase(databaseName));
        AddWebhookSecretProtection(services, webhookSecretEncryptionKey, webhookPreviousSecretEncryptionKeys);
        services.AddScoped<IChannelService, PostgresChannelService>();
        services.AddScoped<IMessageAcceptanceService, PostgresMessageAcceptanceService>();
        services.AddScoped<IWorkerGateway, PostgresWorkerGateway>();
        services.AddScoped<IRunRecoveryService, RunRecoveryService>();
        services.AddScoped<ISubscriptionService, PostgresSubscriptionService>();
        services.AddScoped<IHubRetentionService, RetentionService>();
        services.AddSingleton<IWebhookDeliveryClient>(_ => new HttpWebhookDeliveryClient(new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        }));
        services.AddSingleton<IWebhookVerificationClient>(_ => new HttpWebhookVerificationClient(new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        }));
        services.AddScoped<IWebhookDeliveryDispatcher, WebhookDeliveryDispatcher>();
        return services;
    }

    private static void AddWebhookSecretProtection(IServiceCollection services, string? key, string? previousKeys)
        => services.AddSingleton<IWebhookSecretProtector>(_ => string.IsNullOrWhiteSpace(key)
            ? new UnavailableWebhookSecretProtector()
            : new AesGcmWebhookSecretProtector(
                key,
                (previousKeys ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));

    public static IServiceCollection AddHubArtifactStorage(this IServiceCollection services, ArtifactStorageOptions options)
    {
        services.AddSingleton<IArtifactStore>(_ => new S3ArtifactStore(options));
        return services;
    }
}
