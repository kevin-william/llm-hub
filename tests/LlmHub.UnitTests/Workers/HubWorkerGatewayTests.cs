using System.Net;
using System.Text;
using LlmHub.Contracts.Workers;
using LlmHub.OpenCodeWorker;

namespace LlmHub.UnitTests.Workers;

public sealed class HubWorkerGatewayTests
{
    [Fact]
    public async Task GatewayUsesThePublicWorkerEndpointsAndMapsTheirResponses()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"runId\":\"run_1\",\"channelId\":\"chn_1\",\"inputMessageId\":\"msg_1\",\"content\":\"prompt\",\"leaseToken\":\"lease_1\",\"leaseExpiresAt\":\"2026-10-02T12:00:00+00:00\",\"deadline\":null,\"sessionId\":null}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"leaseExpiresAt\":\"2026-10-02T12:01:00+00:00\",\"cancelRequested\":false}") },
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var gateway = new HubWorkerGateway(new HttpClient(handler) { BaseAddress = new Uri("http://hub/") });

        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_1", "opencode", ["code"], "1.0", 1), CancellationToken.None);
        var claim = await gateway.ClaimAsync("worker_1", CancellationToken.None, waitSeconds: 25);
        var heartbeat = await gateway.HeartbeatAsync("run_1", new HeartbeatRequest("lease_1"), CancellationToken.None);
        await gateway.CompleteAsync("run_1", new CompleteRunRequest("lease_1", "done", "ses_1"), CancellationToken.None);
        await gateway.FailAsync("run_2", new FailRunRequest("lease_2", "Transient"), CancellationToken.None);

        Assert.Equal("run_1", claim!.RunId);
        Assert.False(heartbeat.CancelRequested);
        Assert.Equal(
            ["/v1/workers/register", "/v1/workers/worker_1/claims", "/v1/runs/run_1/heartbeat", "/v1/runs/run_1/complete", "/v1/runs/run_2/fail"],
            handler.Paths);
        Assert.Contains("\"waitSeconds\":25", handler.Bodies[1]);
    }

    private static StringContent Json(string content) => new(content, Encoding.UTF8, "application/json");

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responses.Dequeue();
        }
    }
}
