using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmHub.IntegrationTests.Api;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmHub.IntegrationTests.Mcp;

public sealed class McpHubToolsTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task ToolsListExposesTheHubOperations()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new
                {
                    _meta = new Dictionary<string, object?>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                        ["io.modelcontextprotocol/clientCapabilities"] = new { },
                    },
                },
            }),
        };
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/list");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(text.StartsWith("event: message\n", StringComparison.Ordinal), text);
        using var body = JsonDocument.Parse(text["event: message\ndata: ".Length..]);
        var names = body.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray();

        Assert.Contains("list_endpoints", names);
        Assert.Contains("open_channel", names);
        Assert.Contains("send_message", names);
        Assert.Contains("get_message", names);
        Assert.Contains("get_channel_history", names);
        Assert.Contains("get_run", names);
        Assert.Contains("cancel_run", names);
        Assert.Contains("ack_message", names);
        Assert.Contains("subscribe_events", names);
        Assert.Contains("unsubscribe_events", names);
    }

    [Fact]
    public async Task OpenAndSendToolsUseTheSameChannelWorkflow()
    {
        using var client = factory.CreateClient();
        using var opened = await CallToolAsync(client, 1, "open_channel", new { destination = "agent:opencode/default" });
        var openedBody = await ReadSseResponseAsync(opened);
        var openText = openedBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(openText);
        Assert.True(openText!.StartsWith('{'), openText);
        using var channel = JsonDocument.Parse(openText!);
        var channelId = channel.RootElement.GetProperty("channelId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(channelId));

        using var sent = await CallToolAsync(client, 2, "send_message", new
        {
            channelId,
            clientMessageId = "mcp_client_1",
            content = "Analyze this project.",
            awaitResponse = false,
        });
        var sentBody = await ReadSseResponseAsync(sent);
        var sentText = sentBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("runId", sentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEndpointsReturnsRegisteredCapabilities()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            context.Endpoints.Add(new EndpointRecord
            {
                Id = "endpoint_mcp_capabilities", Address = "agent:echo/capabilities", Adapter = "echo",
                CapabilitiesJson = "[\"role:reviewer\",\"files\"]", Status = "online",
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var client = factory.CreateClient();
        using var response = await CallToolAsync(client, 1, "list_endpoints", new { });
        using var body = await ReadSseResponseAsync(response);
        var text = body.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(text);
        using var endpoints = JsonDocument.Parse(text!);
        var endpoint = endpoints.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "endpoint_mcp_capabilities");
        Assert.Contains("role:reviewer", endpoint.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task SubscribeAndUnsubscribeToolsManagePersistentCallbacks()
    {
        using var client = factory.CreateClient();
        using var opened = await CallToolAsync(client, 1, "open_channel", new { destination = "agent:opencode/default" });
        using var openedBody = await ReadSseResponseAsync(opened);
        var openText = openedBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(openText);
        using var channel = JsonDocument.Parse(openText!);
        var channelId = channel.RootElement.GetProperty("channelId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(channelId));

        using var subscribed = await CallToolAsync(client, 2, "subscribe_events", new
        {
            channelId,
            callbackUrl = "https://callback.example.test/hub",
            secret = "mcp-test-secret",
            durationSeconds = 300,
        });
        using var subscribedBody = await ReadSseResponseAsync(subscribed);
        var subscriptionText = subscribedBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(subscriptionText);
        using var subscription = JsonDocument.Parse(subscriptionText!);
        var subscriptionId = subscription.RootElement.GetProperty("subscriptionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(subscriptionId));
        Assert.True(subscription.RootElement.GetProperty("isVerified").GetBoolean());

        using var unsubscribed = await CallToolAsync(client, 3, "unsubscribe_events", new { subscriptionId });
        using var unsubscribedBody = await ReadSseResponseAsync(unsubscribed);
        Assert.False(unsubscribedBody.RootElement.GetProperty("result").TryGetProperty("isError", out var isError) && isError.GetBoolean());
    }

    [Fact]
    public async Task PrincipalOutsideChannelCannotReadOrCancelMcpResources()
    {
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Principal-Id", "principal:mcp-owner");
        using var opened = await CallToolAsync(owner, 1, "open_channel", new { destination = "agent:opencode/default" });
        using var openedBody = await ReadSseResponseAsync(opened);
        var openText = openedBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(openText);
        using var channel = JsonDocument.Parse(openText!);
        var channelId = channel.RootElement.GetProperty("channelId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(channelId));

        using var sent = await CallToolAsync(owner, 2, "send_message", new
        {
            channelId,
            clientMessageId = "mcp_access_control",
            content = "private message",
            awaitResponse = false,
        });
        using var sentBody = await ReadSseResponseAsync(sent);
        var sentText = sentBody.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(sentText);
        using var accepted = JsonDocument.Parse(sentText!);
        var messageId = accepted.RootElement.GetProperty("messageId").GetString();
        var runId = accepted.RootElement.GetProperty("runId").GetString();

        using var outsider = factory.CreateClient();
        outsider.DefaultRequestHeaders.Add("X-Principal-Id", "principal:mcp-outsider");
        var restrictedCalls = new[]
        {
            ("get_message", (object)new { messageId }),
            ("get_channel_history", (object)new { channelId, afterSequence = (long?)null, limit = (int?)null }),
            ("get_run", (object)new { runId }),
            ("cancel_run", (object)new { runId }),
        };

        for (var index = 0; index < restrictedCalls.Length; index++)
        {
            var restrictedCall = restrictedCalls[index];
            using var response = await CallToolAsync(outsider, index + 3, restrictedCall.Item1, restrictedCall.Item2);
            using var body = await ReadSseResponseAsync(response);
            var content = body.RootElement.GetProperty("result").GetProperty("content");
            if (restrictedCall.Item1 == "get_channel_history")
            {
                using var history = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
                Assert.Empty(history.RootElement.GetProperty("messages").EnumerateArray());
                continue;
            }

            Assert.Empty(content.EnumerateArray());
        }
    }

    private static async Task<HttpResponseMessage> CallToolAsync(HttpClient client, int id, string name, object arguments)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id,
                method = "tools/call",
                @params = new
                {
                    name,
                    arguments,
                    _meta = new Dictionary<string, object?>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                        ["io.modelcontextprotocol/clientCapabilities"] = new { },
                    },
                },
            }),
        };
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", name);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadSseResponseAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(text.StartsWith("event: message\n", StringComparison.Ordinal), text);
        return JsonDocument.Parse(text["event: message\ndata: ".Length..]);
    }
}
