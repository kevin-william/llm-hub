using LlmHub.Application.Maintenance;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Maintenance;

public sealed record RetentionOptions(int AuditDays = 90, int WebhookDeliveryDays = 30)
{
    public static readonly RetentionOptions Default = new();
}

public sealed class RetentionService(IDbContextFactory<HubDbContext> contextFactory, RetentionOptions? options = null) : IHubRetentionService
{
    private readonly RetentionOptions options = options ?? RetentionOptions.Default;

    public async Task<RetentionResult> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var audit = await context.AuditEvents.Where(item => item.OccurredAt < now.AddDays(-options.AuditDays)).ToArrayAsync(cancellationToken);
        var deliveries = await context.WebhookDeliveries.Where(item =>
                (item.State == "accepted" || item.State == "dead")
                && (item.AcceptedAt ?? item.NextAttemptAt ?? DateTimeOffset.MaxValue) < now.AddDays(-options.WebhookDeliveryDays))
            .ToArrayAsync(cancellationToken);
        context.AuditEvents.RemoveRange(audit);
        context.WebhookDeliveries.RemoveRange(deliveries);
        await context.SaveChangesAsync(cancellationToken);
        return new RetentionResult(audit.Length, deliveries.Length);
    }
}
