using System.Net;
using System.Net.Http.Json;
using LlmHub.Application.Workers;
using LlmHub.Contracts.Workers;

namespace LlmHub.OpenCodeWorker;

public sealed class HubWorkerGateway(HttpClient client) : IWorkerGateway
{
    public async Task RegisterAsync(RegisterWorkerRequest request, CancellationToken cancellationToken, string tenantId = "tenant:development")
        => await SendNoContentAsync(HttpMethod.Post, "v1/workers/register", request, cancellationToken);

    public async Task<ClaimedRunResponse?> ClaimAsync(string workerId, CancellationToken cancellationToken, string tenantId = "tenant:development", int? waitSeconds = null)
    {
        using var response = await client.PostAsJsonAsync($"v1/workers/{Uri.EscapeDataString(workerId)}/claims", new ClaimRequest(waitSeconds), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ClaimedRunResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Hub returned an empty claim response.");
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(string runId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync($"v1/runs/{Uri.EscapeDataString(runId)}/heartbeat", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Hub returned an empty heartbeat response.");
    }

    public Task CompleteAsync(string runId, CompleteRunRequest request, CancellationToken cancellationToken)
        => SendNoContentAsync(HttpMethod.Post, $"v1/runs/{Uri.EscapeDataString(runId)}/complete", request, cancellationToken);

    public Task FailAsync(string runId, FailRunRequest request, CancellationToken cancellationToken)
        => SendNoContentAsync(HttpMethod.Post, $"v1/runs/{Uri.EscapeDataString(runId)}/fail", request, cancellationToken);

    private async Task SendNoContentAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Hub returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }
}
