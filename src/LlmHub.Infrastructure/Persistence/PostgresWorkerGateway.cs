using System.Text.Json;
using LlmHub.Application.Workers;
using LlmHub.Contracts.Artifacts;
using LlmHub.Contracts.Workers;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresWorkerGateway(IDbContextFactory<HubDbContext> contextFactory) : IWorkerGateway
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    public async Task RegisterAsync(RegisterWorkerRequest request, CancellationToken cancellationToken, string tenantId = "tenant:development")
    {
        if (string.IsNullOrWhiteSpace(request.WorkerId) || string.IsNullOrWhiteSpace(request.Adapter) || request.MaxConcurrency < 1)
        {
            throw new DomainException("Worker id, adapter and positive concurrency are required.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var worker = await context.Workers.SingleOrDefaultAsync(item => item.Id == request.WorkerId, cancellationToken);
        if (worker is null)
        {
            worker = new WorkerRecord { Id = request.WorkerId, RegisteredAt = DateTimeOffset.UtcNow };
            context.Workers.Add(worker);
        }

        worker.Adapter = request.Adapter;
        worker.TenantId = tenantId;
        worker.CapabilitiesJson = JsonSerializer.Serialize(request.Capabilities);
        worker.Version = request.Version;
        worker.MaxConcurrency = request.MaxConcurrency;
        var endpoint = await context.Endpoints.SingleOrDefaultAsync(endpoint => endpoint.Id == request.WorkerId, cancellationToken);
        if (endpoint is null)
        {
            endpoint = new EndpointRecord { Id = request.WorkerId };
            context.Endpoints.Add(endpoint);
        }

        endpoint.Address = $"agent:{request.Adapter}/{request.WorkerId}";
        endpoint.TenantId = tenantId;
        endpoint.Adapter = request.Adapter;
        endpoint.CapabilitiesJson = worker.CapabilitiesJson;
        endpoint.Status = "online";
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClaimedRunResponse?> ClaimAsync(string workerId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var worker = await context.Workers.SingleOrDefaultAsync(item => item.Id == workerId, cancellationToken)
            ?? throw new DomainException("The worker is not registered.");
        var activeCount = await context.Runs.CountAsync(
            run => run.WorkerId == workerId && (run.State == RunState.Leased || run.State == RunState.Running), cancellationToken);
        if (activeCount >= worker.MaxConcurrency)
        {
            return null;
        }

        var run = await (from queuedRun in context.Runs
                         join channel in context.Channels on queuedRun.ChannelId equals channel.Id
                         where queuedRun.State == RunState.Queued && queuedRun.Adapter == worker.Adapter && channel.TenantId == worker.TenantId
                         orderby queuedRun.AcceptedAt
                         select queuedRun).FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        run.State = RunState.Leased;
        run.WorkerId = workerId;
        run.LeaseToken = Guid.NewGuid().ToString("N");
        run.LeaseExpiresAt = now.Add(LeaseDuration);
        run.Version = Guid.NewGuid();
        var attemptId = $"att_{Guid.NewGuid():N}";
        var attemptNumber = (await context.RunAttempts
            .Where(attempt => attempt.RunId == run.Id)
            .Select(attempt => (int?)attempt.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        context.RunAttempts.Add(new RunAttemptRecord
        {
            Id = attemptId,
            RunId = run.Id,
            Number = attemptNumber,
            LeaseToken = run.LeaseToken,
            LeaseExpiresAt = run.LeaseExpiresAt,
        });
        context.AuditEvents.Add(new AuditRecord
        {
            Id = $"aud_{Guid.NewGuid():N}", EventName = "run.claimed", ChannelId = run.ChannelId,
            RunId = run.Id, OccurredAt = now,
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
        using var activity = HubTelemetry.Start("run.claim", run.ChannelId, runId: run.Id, attemptId: attemptId);
        var input = await context.Messages.SingleAsync(item => item.Id == run.InputMessageId, cancellationToken);
        var session = await context.AgentSessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ChannelId == run.ChannelId && item.Adapter == run.Adapter,
            cancellationToken);
        return new ClaimedRunResponse(run.Id, run.ChannelId, run.InputMessageId, input.Content, run.LeaseToken, run.LeaseExpiresAt.Value, run.Deadline, session?.ExternalSessionId);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(string runId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        var run = await ValidateLeaseAsync(runId, request.LeaseToken, cancellationToken);
        run.State = RunState.Running;
        run.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(LeaseDuration);
        await SaveAsync(run, cancellationToken);
        return new HeartbeatResponse(run.LeaseExpiresAt.Value, run.CancelRequested);
    }

    public async Task CompleteAsync(string runId, CompleteRunRequest request, CancellationToken cancellationToken)
    {
        using var activity = HubTelemetry.Start("run.complete", runId: runId);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var trackedRun = await context.Runs.SingleOrDefaultAsync(
                item => item.Id == runId,
                cancellationToken)
            ?? throw new DomainException("The run does not exist.");
        if (trackedRun.State == RunState.Succeeded)
        {
            return;
        }

        if (trackedRun.LeaseToken != request.LeaseToken || trackedRun.LeaseExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("The lease token is no longer valid.");
        }

        var input = await context.Messages.SingleAsync(item => item.Id == trackedRun.InputMessageId, cancellationToken);
        var channel = await context.Channels.SingleAsync(item => item.Id == trackedRun.ChannelId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var messageId = $"msg_{Guid.NewGuid():N}";
        channel.LastSequence++;
        channel.Version = Guid.NewGuid();
        trackedRun.State = RunState.Succeeded;
        trackedRun.LeaseToken = null;
        trackedRun.LeaseExpiresAt = null;
        trackedRun.Version = Guid.NewGuid();
        context.Messages.Add(new MessageRecord
        {
            Id = messageId,
            ChannelId = trackedRun.ChannelId,
            Sequence = channel.LastSequence,
            PrincipalId = input.Recipient,
            ClientMessageId = $"run-result:{trackedRun.Id}",
            Sender = input.Recipient,
            Recipient = input.Sender,
            Content = request.Content ?? string.Empty,
            RootMessageId = input.RootMessageId,
            ReplyToMessageId = input.Id,
            CausationId = input.Id,
            HopCount = input.HopCount + 1,
            CreatedAt = now,
        });
        context.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = $"evt_{Guid.NewGuid():N}",
            EventName = "message.created",
            AggregateId = messageId,
            Payload = JsonSerializer.Serialize(new { messageId, channelId = trackedRun.ChannelId, runId = trackedRun.Id }),
            OccurredAt = now,
        });
        context.AuditEvents.Add(new AuditRecord
        {
            Id = $"aud_{Guid.NewGuid():N}", EventName = "run.completed", ChannelId = trackedRun.ChannelId,
            MessageId = messageId, RunId = trackedRun.Id, OccurredAt = now,
        });
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var session = await context.AgentSessions.SingleOrDefaultAsync(
                item => item.ChannelId == trackedRun.ChannelId && item.Adapter == trackedRun.Adapter,
                cancellationToken);
            if (session is null)
            {
                context.AgentSessions.Add(new AgentSessionRecord
                {
                    Id = $"ses_{Guid.NewGuid():N}",
                    ChannelId = trackedRun.ChannelId,
                    Adapter = trackedRun.Adapter,
                    ExternalSessionId = request.SessionId,
                });
            }
            else
            {
                session.ExternalSessionId = request.SessionId;
            }
        }

        foreach (var artifact in request.Artifacts ?? [])
        {
            ValidateArtifact(artifact);
            var existing = await context.Artifacts.SingleOrDefaultAsync(
                item => item.RunId == trackedRun.Id && item.ContentHash == artifact.ContentHash,
                cancellationToken);
            if (existing is null)
            {
                context.Artifacts.Add(new ArtifactRecord
                {
                    Id = $"art_{Guid.NewGuid():N}",
                    RunId = trackedRun.Id,
                    ChannelId = trackedRun.ChannelId,
                    StorageKey = artifact.StorageKey,
                    ContentHash = artifact.ContentHash,
                    ContentType = artifact.ContentType,
                    Length = artifact.Length,
                });
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        HubTelemetry.RunsCompleted.Add(1);
    }

    public async Task FailAsync(string runId, FailRunRequest request, CancellationToken cancellationToken)
    {
        var run = await ValidateLeaseAsync(runId, request.LeaseToken, cancellationToken);
        if (!Enum.TryParse<FailureKind>(request.FailureKind, true, out var failureKind))
        {
            throw new DomainException("The failure kind is invalid.");
        }

        run.FailureKind = failureKind;
        run.State = failureKind switch
        {
            FailureKind.Transient => RunState.RetryWait,
            FailureKind.Cancelled => RunState.Cancelled,
            _ => RunState.Failed,
        };
        run.LeaseToken = null;
        run.LeaseExpiresAt = null;
        await SaveAsync(run, cancellationToken);
    }

    private async Task<RunRecord> ValidateLeaseAsync(string runId, string leaseToken, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken)
            ?? throw new DomainException("The run does not exist.");
        if (run.LeaseToken != leaseToken || run.LeaseExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("The lease token is no longer valid.");
        }

        return run;
    }

    private async Task SaveAsync(RunRecord changedRun, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var originalVersion = changedRun.Version;
        changedRun.Version = Guid.NewGuid();
        var entry = context.Runs.Update(changedRun);
        entry.Property(run => run.Version).OriginalValue = originalVersion;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateArtifact(ArtifactReference artifact)
    {
        if (artifact.Length is < 1 or > 20 * 1024 * 1024
            || !System.Text.RegularExpressions.Regex.IsMatch(artifact.ContentHash, "^[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || artifact.StorageKey != $"sha256/{artifact.ContentHash}")
        {
            throw new DomainException("The artifact metadata is invalid.");
        }
    }
}
