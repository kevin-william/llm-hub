using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmHub.IntegrationTests.Api;

namespace LlmHub.IntegrationTests.Mcp;

public sealed class McpHubToolsTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task ToolsListExposesTheEightHubOperations()
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
