using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LlmHub.Contracts.Adapters;

namespace LlmHub.OpenCodeWorker;

public sealed record OpenCodeServerOptions(string? Username = null, string? Password = null);

public sealed class OpenCodeAdapter(HttpClient client, OpenCodeServerOptions options) : IAgentAdapter
{
    private readonly ConcurrentDictionary<string, string> runSessions = new(StringComparer.Ordinal);

    public async Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
    {
        var sessionId = input.SessionId ?? await CreateSessionAsync(input.ChannelId, cancellationToken);
        runSessions[input.RunId] = sessionId;
        try
        {
            using var response = await SendAsync(HttpMethod.Post, $"session/{Uri.EscapeDataString(sessionId)}/message", new { parts = new[] { new { type = "text", text = input.Content } }, }, cancellationToken);
            var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("OpenCode returned an empty message response.");
            using (document)
            {
                return new ExecutionResult(ReadText(document.RootElement), [], sessionId);
            }
        }
        finally
        {
            runSessions.TryRemove(input.RunId, out _);
        }
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        if (!runSessions.TryGetValue(runId, out var sessionId))
        {
            return;
        }

        using var response = await SendAsync(HttpMethod.Post, $"session/{Uri.EscapeDataString(sessionId)}/abort", null, cancellationToken);
    }

    public async Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "global/health", null, cancellationToken);
            var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            if (document is null)
            {
                return new AdapterHealth(false, "OpenCode returned an empty health response.");
            }

            using (document)
            {
                var healthy = document.RootElement.TryGetProperty("healthy", out var value) && value.ValueKind == JsonValueKind.True;
                var version = document.RootElement.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
                return new AdapterHealth(healthy, healthy ? $"OpenCode {version ?? "server"} is ready." : "OpenCode reported itself unhealthy.");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new AdapterHealth(false, $"OpenCode is unavailable: {exception.Message}");
        }
    }

    private async Task<string> CreateSessionAsync(string channelId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "session", new { title = $"Hub {channelId}" }, cancellationToken);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenCode returned an empty session response.");
        using (document)
        {
            if (!document.RootElement.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw new InvalidOperationException("OpenCode did not return a session id.");
            }

            return id.GetString()!;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username ?? "opencode"}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        throw new HttpRequestException($"OpenCode returned {(int)response.StatusCode}: {detail}");
    }

    private static string ReadText(JsonElement root)
    {
        if (!root.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenCode message response has no parts.");
        }

        return string.Concat(parts.EnumerateArray()
            .Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrEmpty(text)));
    }
}
