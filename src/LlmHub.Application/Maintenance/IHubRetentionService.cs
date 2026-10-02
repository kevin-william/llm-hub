namespace LlmHub.Application.Maintenance;

public sealed record RetentionResult(int AuditEventsRemoved, int WebhookDeliveriesRemoved);

public interface IHubRetentionService
{
    Task<RetentionResult> PurgeAsync(CancellationToken cancellationToken);
}
