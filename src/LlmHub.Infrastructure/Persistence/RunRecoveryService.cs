using LlmHub.Application.Workers;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Persistence;

public sealed class RunRecoveryService(IDbContextFactory<HubDbContext> contextFactory) : IRunRecoveryService
{
    private const int MaximumAttempts = 3;

    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expired = await context.Runs.Where(run =>
                (run.State == RunState.Leased || run.State == RunState.Running)
                && run.LeaseExpiresAt < now)
            .ToArrayAsync(cancellationToken);
        foreach (var run in expired)
        {
            run.AttemptCount++;
            run.State = run.AttemptCount >= MaximumAttempts ? RunState.DeadLetter : RunState.RetryWait;
            run.RetryAt = run.State == RunState.RetryWait
                ? now.AddSeconds(Math.Min(60, 5 * run.AttemptCount))
                : null;
            run.LeaseToken = null;
            run.LeaseExpiresAt = null;
            run.WorkerId = null;
            run.Version = Guid.NewGuid();
            context.AuditEvents.Add(new AuditRecord
            {
                Id = $"aud_{Guid.NewGuid():N}", EventName = "lease.expired", ChannelId = run.ChannelId,
                RunId = run.Id, OccurredAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        HubTelemetry.ExpiredLeases.Add(expired.Length);
        return expired.Length;
    }

    public async Task<int> EnqueueReadyRetriesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var retries = await context.Runs.Where(run => run.State == RunState.RetryWait && run.RetryAt <= now)
            .ToArrayAsync(cancellationToken);
        foreach (var run in retries)
        {
            run.State = RunState.Queued;
            run.RetryAt = null;
            run.Version = Guid.NewGuid();
        }

        await context.SaveChangesAsync(cancellationToken);
        return retries.Length;
    }
}
