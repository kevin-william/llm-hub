using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmHub.Application.Messaging;
using LlmHub.Application.Workers;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Events;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Workers;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LlmHub.EndToEndTests;

public sealed class ChatGptOpenCodeFlowTests(ChatGptOpenCodeFactory factory) : IClassFixture<ChatGptOpenCodeFactory>
{
    [Fact]
    public async Task SubscriptionBeforeMessageReceivesPersistedWorkerResponse()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", "principal:chatgpt/e2e-user");
        var channel = await (await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default")))
            .Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);
        var subscribe = await client.PostAsJsonAsync("/v1/subscriptions", new CreateSubscriptionRequest(
            channel!.ChannelId, "message.created", "https://callback.example.test/events", "whsec_e2e", 300));
        Assert.Equal(HttpStatusCode.Created, subscribe.StatusCode);

        var send = await client.PostAsJsonAsync($"/v1/channels/{channel.ChannelId}/messages", new SendMessageRequest("e2e-client-message", "Explain the result.", true));
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        var accepted = await send.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(accepted);

        using (var scope = factory.Services.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest("worker-e2e", "opencode", [], "fake", 1), CancellationToken.None);
            var claim = await gateway.ClaimAsync("worker-e2e", CancellationToken.None);
            Assert.NotNull(claim);
            await gateway.HeartbeatAsync(claim!.RunId, new HeartbeatRequest(claim.LeaseToken), CancellationToken.None);
            await gateway.CompleteAsync(claim.RunId, new CompleteRunRequest(claim.LeaseToken, "The run completed."), CancellationToken.None);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryDispatcher>();
            await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None);
            await dispatcher.DeliverPendingAsync(CancellationToken.None);
        }

        var response = await client.GetFromJsonAsync<MessageResponse>($"/v1/messages/{accepted!.MessageId}");
        Assert.Equal("Explain the result.", response?.Content);
        using var callbackPayload = JsonDocument.Parse(Assert.Single(factory.Callbacks.Payloads));
        Assert.Equal("message.created", callbackPayload.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("The run completed.", (await GetReplyAsync(factory, accepted.RunId)).Content);
    }

    [Fact]
    public async Task AwaitResponseBeforeSubscriptionIsRejected()
    {
        using var client = factory.CreateClient();
        var channel = await (await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default")))
            .Content.ReadFromJsonAsync<ChannelResponse>();

        var send = await client.PostAsJsonAsync($"/v1/channels/{channel!.ChannelId}/messages", new SendMessageRequest("e2e-no-subscription", "Must fail.", true));

        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Contains("SUBSCRIPTION_REQUIRED", await send.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<MessageRecord> GetReplyAsync(ChatGptOpenCodeFactory factory, string runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        return await context.Messages.SingleAsync(message => message.ClientMessageId == $"run-result:{runId}");
    }
}

public sealed class ChatGptOpenCodeFactory : WebApplicationFactory<Program>
{
    public RecordingWebhookClient Callbacks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Hub:PersistenceProvider", "InMemory");
        builder.UseSetting("Hub:InMemoryDatabase", $"hub-e2e-{Guid.NewGuid():N}");
        builder.UseSetting("Webhook:SecretEncryptionKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebhookDeliveryClient>();
            services.AddSingleton<IWebhookDeliveryClient>(Callbacks);
            services.RemoveAll<IWebhookVerificationClient>();
            services.AddSingleton<IWebhookVerificationClient>(new VerifiedWebhookClient());
        });
    }
}

public sealed class RecordingWebhookClient : IWebhookDeliveryClient
{
    public List<string> Payloads { get; } = [];

    public Task<HttpStatusCode> PostAsync(Uri callback, string payload, WebhookSignature signature, CancellationToken cancellationToken)
    {
        Payloads.Add(payload);
        return Task.FromResult(HttpStatusCode.NoContent);
    }
}

public sealed class VerifiedWebhookClient : IWebhookVerificationClient
{
    public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(true);
}
