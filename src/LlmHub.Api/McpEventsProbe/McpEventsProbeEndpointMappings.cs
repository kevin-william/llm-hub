using System.Text.Json;

namespace LlmHub.Api.McpEventsProbe;

public static class McpEventsProbeEndpointMappings
{
    private const string ProtocolVersion = "2026-07-28";

    public static void MapMcpEventsProbe(this WebApplication app)
    {
        app.MapPost("/mcp/events-probe", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(McpJsonRpcRequest request, HttpRequest httpRequest, IMcpEventsProbeService service, CancellationToken cancellationToken)
    {
        if (httpRequest.Headers["MCP-Protocol-Version"] != ProtocolVersion || httpRequest.Headers["Mcp-Method"] != request.Method)
        {
            return Error(request.Id, -32020, "MCP headers do not match the request.");
        }

        try
        {
            return request.Method switch
            {
                "events/list" => Result(request.Id, new { events = service.ListEvents() }),
                "events/subscribe" => Result(request.Id, await SubscribeAsync(request.Params, service, cancellationToken)),
                "events/unsubscribe" => Result(request.Id, new { removed = service.Unsubscribe(GetString(request.Params, "subscriptionId")) }),
                "events/publish" => Result(request.Id, await PublishAsync(request.Params, service, cancellationToken)),
                _ => Error(request.Id, -32601, "Method not found."),
            };
        }
        catch (ArgumentException exception)
        {
            return Error(request.Id, -32602, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Error(request.Id, -32000, exception.Message);
        }
    }

    private static async Task<McpEventSubscription> SubscribeAsync(JsonElement parameters, IMcpEventsProbeService service, CancellationToken cancellationToken) =>
        await service.SubscribeAsync(
            GetString(parameters, "callbackUrl"),
            GetString(parameters, "secret"),
            GetInt32(parameters, "durationSeconds"),
            cancellationToken);

    private static async Task<McpEventDispatchResult> PublishAsync(JsonElement parameters, IMcpEventsProbeService service, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("data", out var data))
        {
            throw new ArgumentException("data is required.");
        }

        return await service.PublishAsync(
            GetString(parameters, "eventName"),
            data,
            parameters.TryGetProperty("eventId", out var eventId) ? eventId.GetString() : null,
            cancellationToken);
    }

    private static string GetString(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new ArgumentException($"{name} is required.")
            : throw new ArgumentException($"{name} is required.");

    private static int GetInt32(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new ArgumentException($"{name} must be an integer.");

    private static IResult Result(JsonElement id, object value) =>
        Results.Json(new { jsonrpc = "2.0", id, result = value });

    private static IResult Error(JsonElement id, int code, string message) =>
        Results.Json(new { jsonrpc = "2.0", id, error = new { code, message } });
}

public sealed record McpJsonRpcRequest(JsonElement Id, string Method, JsonElement Params);
