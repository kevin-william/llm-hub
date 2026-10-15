using System.Net.Http.Headers;
using LlmHub.Application.Workers;
using LlmHub.Contracts.Adapters;
using LlmHub.OpenCodeWorker;

var builder = Host.CreateApplicationBuilder(args);
var hubUrl = RequireUrl(builder.Configuration["HubWorker:BaseUrl"], "HubWorker:BaseUrl");
var openCodeUrl = RequireUrl(builder.Configuration["OpenCode:BaseUrl"], "OpenCode:BaseUrl");
var workerToken = builder.Configuration["HubWorker:BearerToken"];
builder.Services.AddSingleton(new OpenCodeServerOptions(
    builder.Configuration["OpenCode:Username"],
    builder.Configuration["OpenCode:Password"]));
builder.Services.AddHttpClient<IAgentAdapter, OpenCodeAdapter>(client =>
{
    client.BaseAddress = openCodeUrl;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<IWorkerGateway, HubWorkerGateway>(client =>
{
    client.BaseAddress = hubUrl;
    client.Timeout = Timeout.InfiniteTimeSpan;
    if (!string.IsNullOrWhiteSpace(workerToken))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);
    }
});
builder.Services.AddSingleton(new OpenCodeWorkerOptions(
    builder.Configuration["OpenCodeWorker:WorkerId"] ?? $"opencode-{Environment.MachineName}",
    builder.Configuration["OpenCodeWorker:Version"] ?? "unknown",
    Math.Clamp(builder.Configuration.GetValue<int?>("OpenCodeWorker:PollSeconds") ?? 2, 1, 30),
    Math.Clamp(builder.Configuration.GetValue<int?>("OpenCodeWorker:HeartbeatSeconds") ?? 15, 1, 30),
    Math.Clamp(builder.Configuration.GetValue<int?>("OpenCodeWorker:ClaimWaitSeconds") ?? 25, 0, 30)));
builder.Services.AddHostedService<OpenCodeWorker>();

var host = builder.Build();
host.Run();

static Uri RequireUrl(string? value, string key)
    => Uri.TryCreate(value, UriKind.Absolute, out var url)
        ? url
        : throw new InvalidOperationException($"{key} must be an absolute URL.");
