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
using LlmHub.Workers.FakeWorker;
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

    [Fact]
    public async Task RepeatedDeliveryProcessingDoesNotDuplicateThePersistedResponseOrCallback()
    {
        var principal = $"principal:chatgpt/e2e-duplicate-{Guid.NewGuid():N}";
        var callbackCount = factory.Callbacks.Payloads.Count;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", principal);
        var channel = await (await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default")))
            .Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);
        var subscribe = await client.PostAsJsonAsync("/v1/subscriptions", new CreateSubscriptionRequest(
            channel!.ChannelId, "message.created", "https://callback.example.test/duplicate", "whsec_duplicate", 300));
        Assert.Equal(HttpStatusCode.Created, subscribe.StatusCode);

        var send = await client.PostAsJsonAsync($"/v1/channels/{channel.ChannelId}/messages", new SendMessageRequest(
            $"e2e-duplicate-{Guid.NewGuid():N}", "Deliver once.", true));
        var accepted = await send.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(accepted);

        using (var scope = factory.Services.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest("worker-e2e-duplicate", "opencode", [], "fake", 1), CancellationToken.None);
            var claim = await gateway.ClaimAsync("worker-e2e-duplicate", CancellationToken.None);
            Assert.NotNull(claim);
            await gateway.HeartbeatAsync(claim!.RunId, new HeartbeatRequest(claim.LeaseToken), CancellationToken.None);
            var completion = new CompleteRunRequest(claim.LeaseToken, "Delivered once.");
            await gateway.CompleteAsync(claim.RunId, completion, CancellationToken.None);
            await gateway.CompleteAsync(claim.RunId, completion, CancellationToken.None);

            var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryDispatcher>();
            await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None);
            await dispatcher.CreatePendingDeliveriesAsync(CancellationToken.None);
            await dispatcher.DeliverPendingAsync(CancellationToken.None);
            await dispatcher.DeliverPendingAsync(CancellationToken.None);
        }

        Assert.Equal(callbackCount + 1, factory.Callbacks.Payloads.Count);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var contextFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Assert.Single(await context.Messages.Where(message => message.CausationId == accepted!.MessageId).ToListAsync());
    }

    [Fact]
    public async Task SlowCallbackIsReservedBeforeAnotherDispatcherCanSendIt()
    {
        var principal = $"principal:chatgpt/e2e-slow-{Guid.NewGuid():N}";
        var callbackCount = factory.Callbacks.Payloads.Count;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", principal);
        var channel = await (await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default")))
            .Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/subscriptions", new CreateSubscriptionRequest(
            channel!.ChannelId, "message.created", "https://callback.example.test/slow", "whsec_slow", 300))).StatusCode);
        var send = await client.PostAsJsonAsync($"/v1/channels/{channel.ChannelId}/messages", new SendMessageRequest(
            $"e2e-slow-{Guid.NewGuid():N}", "Wait for the callback.", true));
        var accepted = await send.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(accepted);

        await using (var completionScope = factory.Services.CreateAsyncScope())
        {
            var gateway = completionScope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest("worker-e2e-slow", "opencode", [], "fake", 1), CancellationToken.None);
            var claim = await gateway.ClaimAsync("worker-e2e-slow", CancellationToken.None);
            Assert.NotNull(claim);
            await gateway.HeartbeatAsync(claim!.RunId, new HeartbeatRequest(claim.LeaseToken), CancellationToken.None);
            await gateway.CompleteAsync(claim.RunId, new CompleteRunRequest(claim.LeaseToken, "The callback waited."), CancellationToken.None);
            await completionScope.ServiceProvider.GetRequiredService<IWebhookDeliveryDispatcher>().CreatePendingDeliveriesAsync(CancellationToken.None);
        }

        var blockedDelivery = factory.Callbacks.BlockNextDelivery();
        try
        {
            await using var firstScope = factory.Services.CreateAsyncScope();
            var first = firstScope.ServiceProvider.GetRequiredService<IWebhookDeliveryDispatcher>().DeliverPendingAsync(CancellationToken.None);
            await blockedDelivery.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await using var secondScope = factory.Services.CreateAsyncScope();
            var second = await secondScope.ServiceProvider.GetRequiredService<IWebhookDeliveryDispatcher>().DeliverPendingAsync(CancellationToken.None);
            Assert.Equal(0, second);

            blockedDelivery.Release.TrySetResult(true);
            Assert.Equal(1, await first);
        }
        finally
        {
            blockedDelivery.Release.TrySetResult(true);
            factory.Callbacks.ClearBlock(blockedDelivery);
        }

        Assert.Equal(callbackCount + 1, factory.Callbacks.Payloads.Count);
    }

    [Fact]
    public async Task InterruptedWorkerLeaseIsRecoveredAndCompletedOnceByFakeWorker()
    {
        var principal = $"principal:chatgpt/e2e-recovery-{Guid.NewGuid():N}";
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", principal);
        var channel = await (await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default")))
            .Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);
        var sent = await client.PostAsJsonAsync($"/v1/channels/{channel!.ChannelId}/messages", new SendMessageRequest(
            $"e2e-recovery-{Guid.NewGuid():N}", "Recover this run.", false));
        var accepted = await sent.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(accepted);

        await using (var interruptedScope = factory.Services.CreateAsyncScope())
        {
            var gateway = interruptedScope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest("worker-e2e-interrupted", "opencode", [], "fake", 1), CancellationToken.None);
            var claim = await gateway.ClaimAsync("worker-e2e-interrupted", CancellationToken.None);
            Assert.NotNull(claim);

            var contextFactory = interruptedScope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            var run = await context.Runs.SingleAsync(run => run.Id == accepted!.RunId);
            run.LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var recoveryScope = factory.Services.CreateAsyncScope())
        {
            var recovery = recoveryScope.ServiceProvider.GetRequiredService<IRunRecoveryService>();
            Assert.Equal(1, await recovery.RecoverExpiredLeasesAsync(CancellationToken.None));
            var contextFactory = recoveryScope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            var run = await context.Runs.SingleAsync(run => run.Id == accepted!.RunId);
            run.RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync(CancellationToken.None);
            Assert.Equal(1, await recovery.EnqueueReadyRetriesAsync(CancellationToken.None));
        }

        await using (var replacementScope = factory.Services.CreateAsyncScope())
        {
            var gateway = replacementScope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest("worker-e2e-replacement", "opencode", [], "fake", 1), CancellationToken.None);
            Assert.True(await new FakeWorkerExecutor(gateway, "worker-e2e-replacement").ExecuteOneAsync(CancellationToken.None));
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
        await using var verify = await verifyFactory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(LlmHub.Domain.Runs.RunState.Succeeded, (await verify.Runs.SingleAsync(run => run.Id == accepted!.RunId)).State);
        Assert.Single(await verify.Messages.Where(message => message.CausationId == accepted.MessageId).ToListAsync());
        Assert.Equal(2, await verify.RunAttempts.CountAsync(attempt => attempt.RunId == accepted.RunId));
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
    private BlockingDelivery? block;

    public BlockingDelivery BlockNextDelivery()
    {
        block = new BlockingDelivery();
        return block;
    }

    public void ClearBlock(BlockingDelivery expected)
    {
        if (ReferenceEquals(block, expected))
        {
            block = null;
        }
    }

    public async Task<HttpStatusCode> PostAsync(Uri callback, string payload, WebhookSignature signature, CancellationToken cancellationToken)
    {
        Payloads.Add(payload);
        var activeBlock = block;
        if (activeBlock is not null)
        {
            activeBlock.Started.TrySetResult(true);
            await activeBlock.Release.Task.WaitAsync(cancellationToken);
        }

        return HttpStatusCode.NoContent;
    }
}

public sealed class BlockingDelivery
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class VerifiedWebhookClient : IWebhookVerificationClient
{
    public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(true);
}
