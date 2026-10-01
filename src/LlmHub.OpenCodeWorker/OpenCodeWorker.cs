using LlmHub.Application.Workers;
using LlmHub.Contracts.Adapters;
using LlmHub.Contracts.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LlmHub.OpenCodeWorker;

public sealed record OpenCodeWorkerOptions(string WorkerId, string Version, int PollSeconds, int HeartbeatSeconds = 15);

public sealed class OpenCodeWorker(
    IServiceScopeFactory scopeFactory,
    IAgentAdapter adapter,
    OpenCodeWorkerOptions options,
    ILogger<OpenCodeWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> LogUnavailable = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, "OpenCodeUnavailable"),
        "OpenCode adapter is unavailable: {Detail}");
    private static readonly Action<ILogger, string, Exception?> LogExecutionFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2, "OpenCodeExecutionFailed"),
        "OpenCode execution failed for run {RunId}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var health = await adapter.HealthAsync(stoppingToken);
        if (!health.IsHealthy)
        {
            LogUnavailable(logger, health.Detail, null);
            return;
        }

        using (var scope = scopeFactory.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IWorkerGateway>();
            await gateway.RegisterAsync(new RegisterWorkerRequest(options.WorkerId, "opencode", [], options.Version, 1), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            ClaimedRunResponse? claim;
            using (var scope = scopeFactory.CreateScope())
            {
                claim = await scope.ServiceProvider.GetRequiredService<IWorkerGateway>().ClaimAsync(options.WorkerId, stoppingToken);
            }

            if (claim is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
                continue;
            }

            using var executionScope = scopeFactory.CreateScope();
            await ProcessClaimAsync(executionScope.ServiceProvider.GetRequiredService<IWorkerGateway>(), claim, stoppingToken);
        }
    }

    public async Task ProcessClaimAsync(IWorkerGateway gateway, ClaimedRunResponse claim, CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (claim.Deadline is { } deadlineAt && deadlineAt > DateTimeOffset.UtcNow)
        {
            deadline.CancelAfter(deadlineAt - DateTimeOffset.UtcNow);
        }

        try
        {
            var heartbeat = await gateway.HeartbeatAsync(claim.RunId, new HeartbeatRequest(claim.LeaseToken), deadline.Token);
            if (heartbeat.CancelRequested)
            {
                await adapter.CancelAsync(claim.RunId, stoppingToken);
                await gateway.FailAsync(claim.RunId, new FailRunRequest(claim.LeaseToken, "Cancelled"), stoppingToken);
                return;
            }

            var execution = adapter.ExecuteAsync(new ExecutionInput(claim.RunId, claim.ChannelId, claim.Content, claim.Deadline, [], claim.SessionId), deadline.Token);
            var monitor = MonitorHeartbeatsAsync(gateway, claim, deadline.Token);
            var completed = await Task.WhenAny(execution, monitor);
            if (completed == monitor && await monitor)
            {
                await adapter.CancelAsync(claim.RunId, CancellationToken.None);
                deadline.Cancel();
                try
                {
                    await execution;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the expected result after the Hub revoked the run.
                }

                await gateway.FailAsync(claim.RunId, new FailRunRequest(claim.LeaseToken, "Cancelled"), CancellationToken.None);
                return;
            }

            var result = await execution;
            deadline.Cancel();
            await IgnoreCancellationAsync(monitor);
            await gateway.CompleteAsync(claim.RunId, new CompleteRunRequest(claim.LeaseToken, result.Content, result.SessionId, result.ArtifactReferences), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await adapter.CancelAsync(claim.RunId, CancellationToken.None);
            await gateway.FailAsync(claim.RunId, new FailRunRequest(claim.LeaseToken, "Cancelled"), CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogExecutionFailed(logger, claim.RunId, exception);
            await adapter.CancelAsync(claim.RunId, CancellationToken.None);
            await gateway.FailAsync(claim.RunId, new FailRunRequest(claim.LeaseToken, "Transient"), CancellationToken.None);
        }
    }

    private async Task<bool> MonitorHeartbeatsAsync(IWorkerGateway gateway, ClaimedRunResponse claim, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.HeartbeatSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var heartbeat = await gateway.HeartbeatAsync(claim.RunId, new HeartbeatRequest(claim.LeaseToken), cancellationToken);
            if (heartbeat.CancelRequested)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // The work has completed and the monitor was intentionally stopped.
        }
    }
}
