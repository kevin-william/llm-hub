namespace LlmHub.Application.Workers;

public interface IRunRecoveryService
{
    Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken);

    Task<int> EnqueueReadyRetriesAsync(CancellationToken cancellationToken);
}
