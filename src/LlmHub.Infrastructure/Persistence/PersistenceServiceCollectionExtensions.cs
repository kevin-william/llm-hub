using LlmHub.Application.Channels;
using LlmHub.Application.Events;
using LlmHub.Application.Messaging;
using LlmHub.Application.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LlmHub.Infrastructure.Webhooks;
using LlmHub.Infrastructure.Artifacts;

namespace LlmHub.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddHubPersistence(this IServiceCollection services, string connectionString, string? webhookSecretEncryptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<HubDbContext>(options => options.UseNpgsql(connectionString));
        AddWebhookSecretProtection(services, webhookSecretEncryptionKey);
        services.AddScoped<IChannelService, PostgresChannelService>();
        services.AddScoped<IMessageAcceptanceService, PostgresMessageAcceptanceService>();
        services.AddScoped<IWorkerGateway, PostgresWorkerGateway>();
        services.AddScoped<IRunRecoveryService, RunRecoveryService>();
        services.AddScoped<ISubscriptionService, PostgresSubscriptionService>();
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

    public static IServiceCollection AddHubInMemoryPersistence(this IServiceCollection services, string databaseName, string? webhookSecretEncryptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        services.AddDbContextFactory<HubDbContext>(options => options.UseInMemoryDatabase(databaseName));
        AddWebhookSecretProtection(services, webhookSecretEncryptionKey);
        services.AddScoped<IChannelService, PostgresChannelService>();
        services.AddScoped<IMessageAcceptanceService, PostgresMessageAcceptanceService>();
        services.AddScoped<IWorkerGateway, PostgresWorkerGateway>();
        services.AddScoped<IRunRecoveryService, RunRecoveryService>();
        services.AddScoped<ISubscriptionService, PostgresSubscriptionService>();
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

    private static void AddWebhookSecretProtection(IServiceCollection services, string? key)
        => services.AddSingleton<IWebhookSecretProtector>(_ => string.IsNullOrWhiteSpace(key)
            ? new UnavailableWebhookSecretProtector()
            : new AesGcmWebhookSecretProtector(key));

    public static IServiceCollection AddHubArtifactStorage(this IServiceCollection services, ArtifactStorageOptions options)
    {
        services.AddSingleton<IArtifactStore>(_ => new S3ArtifactStore(options));
        return services;
    }
}
