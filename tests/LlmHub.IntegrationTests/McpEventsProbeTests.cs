using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmHub.Api.McpEventsProbe;
using LlmHub.IntegrationTests.Api;

namespace LlmHub.IntegrationTests;

public sealed class McpEventsProbeTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task EndpointListsSubscribesPublishesAndUnsubscribesInDevelopment()
    {
        using var client = factory.CreateClient();
        using var events = await InvokeAsync(client, 1, "events/list", new { });
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        var eventNames = (await ReadAsync(events)).RootElement.GetProperty("result").GetProperty("events");
        Assert.Equal("message.created", eventNames[0].GetProperty("name").GetString());

        using var subscription = await InvokeAsync(client, 2, "events/subscribe", new
        {
            callbackUrl = "https://callback.example.test/events",
            secret = "probe-secret",
            durationSeconds = 300,
        });
        var subscriptionId = (await ReadAsync(subscription)).RootElement.GetProperty("result").GetProperty("subscriptionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(subscriptionId));

        using var publication = await InvokeAsync(client, 3, "events/publish", new
        {
            eventName = "message.created",
            eventId = "mcp_evt_test",
            data = new { messageId = "msg_test" },
        });
        Assert.Equal(1, (await ReadAsync(publication)).RootElement.GetProperty("result").GetProperty("accepted").GetInt32());
        var delivery = Assert.Single(factory.ProbeCallbacks.Deliveries);
        Assert.Equal("mcp_evt_test", delivery.EventId);
        Assert.True(McpEventsProbeService.VerifySignature("probe-secret", delivery.Timestamp, delivery.Payload, delivery.Signature));

        using var removal = await InvokeAsync(client, 4, "events/unsubscribe", new { subscriptionId });
        Assert.True((await ReadAsync(removal)).RootElement.GetProperty("result").GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task HeaderMismatchReturnsJsonRpcError()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/events-probe")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "events/list", @params = new { } }),
        };
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "events/subscribe");

        using var response = await client.SendAsync(request);
        var body = await ReadAsync(response);
        Assert.Equal(-32020, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private static async Task<HttpResponseMessage> InvokeAsync(HttpClient client, int id, string method, object parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/events-probe")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id, method, @params = parameters }),
        };
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", method);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
