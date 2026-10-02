using LlmHub.Contracts.Workers;

namespace LlmHub.Application.Workers;

public interface IWorkerGateway
{
    Task RegisterAsync(RegisterWorkerRequest request, CancellationToken cancellationToken, string tenantId = "tenant:development");

    Task<ClaimedRunResponse?> ClaimAsync(string workerId, CancellationToken cancellationToken);

    Task<HeartbeatResponse> HeartbeatAsync(string runId, HeartbeatRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(string runId, CompleteRunRequest request, CancellationToken cancellationToken);

    Task FailAsync(string runId, FailRunRequest request, CancellationToken cancellationToken);
}
