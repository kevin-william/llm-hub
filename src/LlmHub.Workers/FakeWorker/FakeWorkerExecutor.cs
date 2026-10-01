using LlmHub.Application.Workers;
using LlmHub.Contracts.Workers;

namespace LlmHub.Workers.FakeWorker;

public sealed class FakeWorkerExecutor(IWorkerGateway gateway, string workerId)
{
    public async Task<bool> ExecuteOneAsync(CancellationToken cancellationToken)
    {
        var claim = await gateway.ClaimAsync(workerId, cancellationToken);
        if (claim is null)
        {
            return false;
        }

        await gateway.HeartbeatAsync(claim.RunId, new HeartbeatRequest(claim.LeaseToken), cancellationToken);
        await gateway.CompleteAsync(
            claim.RunId,
            new CompleteRunRequest(claim.LeaseToken, $"Fake worker completed run {claim.RunId}."),
            cancellationToken);
        return true;
    }
}
