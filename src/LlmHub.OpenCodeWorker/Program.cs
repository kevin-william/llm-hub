using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LlmHub.Application.Workers;
using LlmHub.Contracts.Adapters;
using LlmHub.Infrastructure.Persistence;
using LlmHub.OpenCodeWorker;

var builder = Host.CreateApplicationBuilder(args);
var postgres = builder.Configuration.GetConnectionString("Hub")
    ?? throw new InvalidOperationException("ConnectionStrings:Hub is required by the OpenCode worker.");
builder.Services.AddHubPersistence(postgres, builder.Configuration["Webhook:SecretEncryptionKey"]);
builder.Services.AddSingleton<IAgentAdapter, OpenCodeAdapter>();
builder.Services.AddSingleton(new OpenCodeWorkerOptions(
    builder.Configuration["OpenCodeWorker:WorkerId"] ?? $"opencode-{Environment.MachineName}",
    builder.Configuration["OpenCodeWorker:Version"] ?? "unconfigured",
    Math.Clamp(builder.Configuration.GetValue<int?>("OpenCodeWorker:PollSeconds") ?? 2, 1, 30),
    Math.Clamp(builder.Configuration.GetValue<int?>("OpenCodeWorker:HeartbeatSeconds") ?? 15, 1, 30)));
builder.Services.AddHostedService<OpenCodeWorker>();

var host = builder.Build();
host.Run();
