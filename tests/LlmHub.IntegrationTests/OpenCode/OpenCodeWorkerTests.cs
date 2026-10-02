using LlmHub.Application.Workers;
using LlmHub.Contracts.Adapters;
using LlmHub.Contracts.Workers;
using LlmHub.OpenCodeWorker;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerService = LlmHub.OpenCodeWorker.OpenCodeWorker;

namespace LlmHub.IntegrationTests.OpenCode;

public sealed class OpenCodeWorkerTests
{
    [Fact]
    public async Task ClaimedRunIsMappedToAdapterAndCompletedWithItsResponse()
    {
        var gateway = new RecordingGateway();
        var adapter = new FakeAdapter();
        var worker = new WorkerService(null!, adapter, new OpenCodeWorkerOptions("worker_1", "test", 1), NullLogger<WorkerService>.Instance);

        await worker.ProcessClaimAsync(gateway, Claim(), CancellationToken.None);

        Assert.Equal("Analyze this project.", adapter.Input!.Content);
        Assert.Equal("run_1", adapter.Input.RunId);
        Assert.Equal("completed", gateway.Completion!.Content);
        Assert.Null(gateway.Failure);
    }

    [Fact]
    public async Task CancellationRequestedByHubCancelsAdapterAndFencesTheRun()
    {
        var gateway = new RecordingGateway(cancelRequested: true);
        var adapter = new FakeAdapter();
        var worker = new WorkerService(null!, adapter, new OpenCodeWorkerOptions("worker_1", "test", 1), NullLogger<WorkerService>.Instance);

        await worker.ProcessClaimAsync(gateway, Claim(), CancellationToken.None);

        Assert.True(adapter.Cancelled);
        Assert.Equal("Cancelled", gateway.Failure!.FailureKind);
        Assert.Null(gateway.Completion);
    }

    [Fact]
    public async Task LongExecutionMaintainsLeaseWithPeriodicHeartbeats()
    {
        var gateway = new RecordingGateway();
        var adapter = new BlockingAdapter();
        var worker = new WorkerService(null!, adapter, new OpenCodeWorkerOptions("worker_1", "test", 1, 1), NullLogger<WorkerService>.Instance);

        var processing = worker.ProcessClaimAsync(gateway, Claim(), CancellationToken.None);
        await gateway.SecondHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(3));
        adapter.Release.TrySetResult(new ExecutionResult("completed", []));
        await processing;

        Assert.True(gateway.HeartbeatCount >= 2);
        Assert.Equal("completed", gateway.Completion!.Content);
    }

    private static ClaimedRunResponse Claim() => new("run_1", "chn_1", "msg_1", "Analyze this project.", "lease_1", DateTimeOffset.UtcNow.AddMinutes(1), null);

    private sealed class FakeAdapter : IAgentAdapter
    {
        public ExecutionInput? Input { get; private set; }
        public bool Cancelled { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
        {
            Input = input;
            return Task.FromResult(new ExecutionResult("completed", []));
        }

        public Task CancelAsync(string runId, CancellationToken cancellationToken)
        {
            Cancelled = true;
            return Task.CompletedTask;
        }

        public Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken) => Task.FromResult(new AdapterHealth(true, "ready"));
    }

    private sealed class BlockingAdapter : IAgentAdapter
    {
        public TaskCompletionSource<ExecutionResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExecutionResult> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
            => await Release.Task.WaitAsync(cancellationToken);

        public Task CancelAsync(string runId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AdapterHealth> HealthAsync(CancellationToken cancellationToken) => Task.FromResult(new AdapterHealth(true, "ready"));
    }

    private sealed class RecordingGateway(bool cancelRequested = false) : IWorkerGateway
    {
        public CompleteRunRequest? Completion { get; private set; }
        public FailRunRequest? Failure { get; private set; }
        public int HeartbeatCount { get; private set; }
        public TaskCompletionSource<bool> SecondHeartbeat { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegisterAsync(RegisterWorkerRequest request, CancellationToken cancellationToken, string tenantId = "tenant:development") => Task.CompletedTask;
        public Task<ClaimedRunResponse?> ClaimAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<ClaimedRunResponse?>(null);
        public Task<HeartbeatResponse> HeartbeatAsync(string runId, HeartbeatRequest request, CancellationToken cancellationToken)
        {
            HeartbeatCount++;
            if (HeartbeatCount >= 2)
            {
                SecondHeartbeat.TrySetResult(true);
            }

            return Task.FromResult(new HeartbeatResponse(DateTimeOffset.UtcNow.AddMinutes(1), cancelRequested));
        }
        public Task CompleteAsync(string runId, CompleteRunRequest request, CancellationToken cancellationToken) { Completion = request; return Task.CompletedTask; }
        public Task FailAsync(string runId, FailRunRequest request, CancellationToken cancellationToken) { Failure = request; return Task.CompletedTask; }
    }
}
