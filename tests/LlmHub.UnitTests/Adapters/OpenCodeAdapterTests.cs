using System.Net;
using System.Text;
using LlmHub.Contracts.Adapters;
using LlmHub.OpenCodeWorker;

namespace LlmHub.UnitTests.Adapters;

public sealed class OpenCodeAdapterTests
{
    private static readonly string[] ExpectedRequestPaths = ["/session", "/session/ses_1/message", "/session/ses_1/message"];

    [Fact]
    public async Task AdapterCreatesSessionSendsPromptAndKeepsSessionForTheNextRun()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"id\":\"ses_1\"}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"parts\":[{\"type\":\"text\",\"text\":\"analysis\"},{\"type\":\"tool\"},{\"type\":\"text\",\"text\":\" complete\"}]}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"parts\":[{\"type\":\"text\",\"text\":\"continued\"}]}") });
        var adapter = new OpenCodeAdapter(new HttpClient(handler) { BaseAddress = new Uri("http://opencode/") }, new OpenCodeServerOptions());

        var first = await adapter.ExecuteAsync(new ExecutionInput("run_1", "chn_1", "prompt", null, []), CancellationToken.None);
        var second = await adapter.ExecuteAsync(new ExecutionInput("run_2", "chn_1", "next", null, [], first.SessionId), CancellationToken.None);

        Assert.Equal("analysis complete", first.Content);
        Assert.Equal("ses_1", first.SessionId);
        Assert.Equal("continued", second.Content);
        Assert.Equal(ExpectedRequestPaths, handler.Paths);
        Assert.Contains("\"text\":\"prompt\"", handler.Bodies[1]);
    }

    [Fact]
    public async Task AdapterUsesHealthAndAbortEndpointsWithConfiguredBasicAuthentication()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"healthy\":true,\"version\":\"1.2.3\"}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"id\":\"ses_1\"}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"parts\":[{\"type\":\"text\",\"text\":\"done\"}]}") },
            new HttpResponseMessage(HttpStatusCode.OK))
        {
            BlockMessageResponses = true,
        };
        var adapter = new OpenCodeAdapter(new HttpClient(handler) { BaseAddress = new Uri("http://opencode/") }, new OpenCodeServerOptions("worker", "secret"));

        var health = await adapter.HealthAsync(CancellationToken.None);
        var execution = adapter.ExecuteAsync(new ExecutionInput("run_1", "chn_1", "prompt", null, []), CancellationToken.None);
        await handler.WaitForMessageAsync();
        await adapter.CancelAsync("run_1", CancellationToken.None);
        await execution;

        Assert.True(health.IsHealthy);
        Assert.Contains("1.2.3", health.Detail);
        Assert.Equal("/session/ses_1/abort", handler.Paths[3]);
        Assert.All(handler.Authorizations, authorization => Assert.Equal("Basic", authorization?.Scheme));
    }

    private static StringContent Json(string content) => new(content, Encoding.UTF8, "application/json");

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        private readonly TaskCompletionSource<bool> messageStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> releaseMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockMessageResponses { get; init; }
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<System.Net.Http.Headers.AuthenticationHeaderValue?> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            Authorizations.Add(request.Headers.Authorization);
            var response = responses.Dequeue();
            if (path.EndsWith("/message", StringComparison.Ordinal) && BlockMessageResponses)
            {
                messageStarted.TrySetResult(true);
                await releaseMessage.Task.WaitAsync(cancellationToken);
            }

            if (path.EndsWith("/abort", StringComparison.Ordinal))
            {
                releaseMessage.TrySetResult(true);
            }

            return response;
        }

        public Task<bool> WaitForMessageAsync() => messageStarted.Task;
    }
}
