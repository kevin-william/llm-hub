using System.Net;
using LlmHub.Api.McpEventsProbe;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LlmHub.IntegrationTests.Api;

public sealed class HubApiFactory : WebApplicationFactory<Program>
{
    public ProbeCallbackClient ProbeCallbacks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Hub:PersistenceProvider", "InMemory");
        builder.UseSetting("Hub:InMemoryDatabase", $"hub-api-{Guid.NewGuid():N}");
        builder.UseSetting("Webhook:SecretEncryptionKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMcpEventsProbeCallbackClient>();
            services.AddSingleton<IMcpEventsProbeCallbackClient>(ProbeCallbacks);
            services.RemoveAll<IWebhookVerificationClient>();
            services.AddSingleton<IWebhookVerificationClient>(new VerifiedWebhookCallback());
        });
    }

    public sealed class ProbeCallbackClient : IMcpEventsProbeCallbackClient
    {
        public List<Delivery> Deliveries { get; } = [];

        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<HttpStatusCode> DeliverAsync(Uri callback, string payload, string eventId, string timestamp, string signature, CancellationToken cancellationToken)
        {
            Deliveries.Add(new Delivery(payload, eventId, timestamp, signature));
            return Task.FromResult(HttpStatusCode.NoContent);
        }
    }

    public sealed record Delivery(string Payload, string EventId, string Timestamp, string Signature);

    private sealed class VerifiedWebhookCallback : IWebhookVerificationClient
    {
        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
