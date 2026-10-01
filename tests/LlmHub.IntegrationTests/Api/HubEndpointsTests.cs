using System.Net;
using System.Net.Http.Json;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Events;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Runs;

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

    private static async Task<ChannelResponse> CreateChannelAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/v1/channels", new OpenChannelRequest("agent:opencode/default"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelResponse>())!;
    }
}
