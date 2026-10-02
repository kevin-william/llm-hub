using System.Net;
using System.Net.Http.Json;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Events;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmHub.IntegrationTests.Api;

public sealed class HubEndpointsTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task ChannelMessageRunAndHistoryFlowIsAvailableOverHttp()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", "principal:chatgpt/user-1");

        var openResponse = await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default"));
        Assert.Equal(HttpStatusCode.Created, openResponse.StatusCode);
        var channel = await openResponse.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);

        var sendRequest = new SendMessageRequest("client_1", "Analyze this project.", false);
        var sendResponse = await client.PostAsJsonAsync($"/v1/channels/{channel.ChannelId}/messages", sendRequest);
        Assert.Equal(HttpStatusCode.Accepted, sendResponse.StatusCode);
        var accepted = await sendResponse.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(accepted);

        var replayResponse = await client.PostAsJsonAsync($"/v1/channels/{channel.ChannelId}/messages", sendRequest);
        var replay = await replayResponse.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.NotNull(replay);
        Assert.True(replay?.IsReplay);
        Assert.Equal(accepted!.RunId, replay!.RunId);

        var message = await client.GetFromJsonAsync<MessageResponse>($"/v1/messages/{accepted.MessageId}");
        var history = await client.GetFromJsonAsync<MessageHistoryResponse>($"/v1/channels/{channel.ChannelId}/messages");
        var run = await client.GetFromJsonAsync<RunResponse>($"/v1/runs/{accepted.RunId}");

        Assert.Equal("Analyze this project.", message?.Content);
        Assert.Single(history?.Messages ?? []);
        Assert.Equal("queued", run?.State);

        var cancelResponse = await client.PostAsync($"/v1/runs/{accepted.RunId}/cancel", null);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal("cancelled", cancelled?.State);
    }

    [Fact]
    public async Task SendUsesTheDestinationPersistedWithTheChannel()
    {
        using var client = factory.CreateClient();
        var openResponse = await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:echo/default"));
        var channel = await openResponse.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);

        var sendResponse = await client.PostAsJsonAsync(
            $"/v1/channels/{channel!.ChannelId}/messages",
            new SendMessageRequest("client_echo", "Use the echo adapter.", false));
        var accepted = await sendResponse.Content.ReadFromJsonAsync<AcceptMessageResult>();
        Assert.Equal(HttpStatusCode.Accepted, sendResponse.StatusCode);
        Assert.NotNull(accepted);

        await using var scope = factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal("agent:echo/default", (await context.Messages.SingleAsync(item => item.Id == accepted!.MessageId)).Recipient);
        Assert.Equal("echo", (await context.Runs.SingleAsync(item => item.Id == accepted.RunId)).Adapter);
    }

    [Fact]
    public async Task AwaitResponseWithoutSubscriptionReturnsConflictCode()
    {
        using var client = factory.CreateClient();
        var channel = await CreateChannelAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/v1/channels/{channel.ChannelId}/messages",
            new SendMessageRequest("client_subscription", "Need a response.", true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SUBSCRIPTION_REQUIRED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifiedSubscriptionAllowsAwaitResponse()
    {
        using var client = factory.CreateClient();
        var channel = await CreateChannelAsync(client);
        var subscriptionResponse = await client.PostAsJsonAsync(
            "/v1/subscriptions",
            new CreateSubscriptionRequest(channel.ChannelId, "message.created", "https://callback.example.test/events", "whsec_test", 300));
        Assert.Equal(HttpStatusCode.Created, subscriptionResponse.StatusCode);

        var sendResponse = await client.PostAsJsonAsync(
            $"/v1/channels/{channel.ChannelId}/messages",
            new SendMessageRequest("client_subscription_ok", "Need a response.", true));

        Assert.Equal(HttpStatusCode.Accepted, sendResponse.StatusCode);
    }

    [Fact]
    public async Task UnknownChannelReturnsConflictWithoutCreatingWork()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/channels/chn_unknown/messages",
            new SendMessageRequest("client_missing", "No channel.", false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PrincipalOutsideChannelCannotReadItsHistory()
    {
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Principal-Id", "principal:owner");
        var channel = await CreateChannelAsync(owner);
        using var outsider = factory.CreateClient();
        outsider.DefaultRequestHeaders.Add("X-Principal-Id", "principal:outsider");

        var response = await outsider.GetAsync($"/v1/channels/{channel.ChannelId}/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChannelParticipantCanReadStoredArtifact()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Principal-Id", "principal:artifact-owner");
        var channel = await CreateChannelAsync(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            context.Artifacts.Add(new ArtifactRecord
            {
                Id = "art_api", RunId = "run_api", ChannelId = channel.ChannelId,
                ContentHash = new string('c', 64), StorageKey = $"sha256/{new string('c', 64)}",
                ContentType = "text/plain", Length = 3,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var response = await client.GetAsync("/v1/artifacts/art_api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"artifact:sha256/{new string('c', 64)}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WorkerCanUploadAnArtifactBeforeCompletingItsRun()
    {
        using var client = factory.CreateClient();
        var content = System.Text.Encoding.UTF8.GetBytes("worker artifact");
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        body.Headers.Add("X-Artifact-Sha256", expectedHash);

        var response = await client.PostAsync("/v1/workers/worker_1/artifacts", body);
        var artifact = await response.Content.ReadFromJsonAsync<LlmHub.Contracts.Artifacts.ArtifactUploadResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(artifact);
        Assert.Equal(expectedHash, artifact!.ContentHash);
        Assert.Equal($"sha256/{expectedHash}", artifact.StorageKey);
        Assert.Contains(factory.ArtifactStore.UploadedArtifacts, item => item.ContentHash == expectedHash);
    }

    private static async Task<ChannelResponse> CreateChannelAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelResponse>())!;
    }
}
