using System.Text.Json;
using LlmHub.Application.Messaging;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Observability;
using LlmHub.Application.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresMessageAcceptanceService : IMessageAcceptanceService
{
    private readonly IDbContextFactory<HubDbContext> contextFactory;
    private readonly HubQuotaOptions quotas;

    public PostgresMessageAcceptanceService(IDbContextFactory<HubDbContext> contextFactory, HubQuotaOptions? quotas = null)
    {
        this.contextFactory = contextFactory;
        this.quotas = quotas ?? HubQuotaOptions.Default;
    }

    public async Task<AcceptMessageResult> AcceptAsync(AcceptMessageCommand command, CancellationToken cancellationToken)
    {
        using var activity = HubTelemetry.Start("message.accept", command.ChannelId);
        if (System.Text.Encoding.UTF8.GetByteCount(command.Content) > quotas.MaxMessageBytes)
        {
            await RecordQuotaRejectionAsync(command.ChannelId, "quota.message.rejected", cancellationToken);
            throw new DomainException("MESSAGE_QUOTA_EXCEEDED");
        }

        if ((command.Attachments?.Count ?? 0) > quotas.MaxAttachments)
        {
            await RecordQuotaRejectionAsync(command.ChannelId, "quota.attachments.rejected", cancellationToken);
            throw new DomainException("ATTACHMENT_QUOTA_EXCEEDED");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var replay = await FindReplayAsync(context, command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var channel = await context.Channels.SingleOrDefaultAsync(channel => channel.Id == command.ChannelId, cancellationToken)
            ?? throw new DomainException("The channel does not exist.");
        if (channel.TenantId != command.TenantId)
        {
            throw new DomainException("CHANNEL_ACCESS_DENIED");
        }
        var isParticipant = await context.ChannelParticipants.AnyAsync(
            participant => participant.ChannelId == channel.Id && participant.PrincipalId == command.PrincipalId,
            cancellationToken);
        if (!isParticipant)
        {
            throw new DomainException("CHANNEL_ACCESS_DENIED");
        }

        if (command.AwaitResponse)
        {
            var subscriptionExists = await context.Subscriptions.AnyAsync(
                subscription => subscription.PrincipalId == command.PrincipalId
                    && subscription.ChannelId == command.ChannelId
                    && subscription.EventName == "message.created"
                    && subscription.IsVerified
                    && subscription.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);

            if (!subscriptionExists)
            {
                throw new DomainException("SUBSCRIPTION_REQUIRED");
            }
        }

        var activeRuns = await (from queuedRun in context.Runs
                                join inputRecord in context.Messages on queuedRun.InputMessageId equals inputRecord.Id
                                where inputRecord.PrincipalId == command.PrincipalId
                                    && (queuedRun.State == RunState.Accepted || queuedRun.State == RunState.Queued || queuedRun.State == RunState.Leased || queuedRun.State == RunState.Running)
                                select queuedRun.Id).CountAsync(cancellationToken);
        if (activeRuns >= quotas.MaxActiveRunsPerPrincipal)
        {
            await RecordQuotaRejectionAsync(command.ChannelId, "quota.runs.rejected", cancellationToken);
            throw new DomainException("RUN_QUOTA_EXCEEDED");
        }

        if (string.Equals(channel.Ordering, "serial", StringComparison.OrdinalIgnoreCase))
        {
            var hasActiveRun = await context.Runs.AnyAsync(
                run => run.ChannelId == channel.Id
                    && (run.State == RunState.Accepted || run.State == RunState.Queued || run.State == RunState.Leased || run.State == RunState.Running),
                cancellationToken);

            if (hasActiveRun)
            {
                throw new DomainException("A serial channel already has an active run.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var destination = channel.Destination;
        var adapter = DeterministicAdapterRouter.Resolve(destination);
        var messageId = $"msg_{Guid.NewGuid():N}";
        var runId = $"run_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        channel.LastSequence++;
        channel.Version = Guid.NewGuid();

        var message = new MessageRecord
        {
            Id = messageId,
            ChannelId = command.ChannelId,
            Sequence = channel.LastSequence,
            PrincipalId = command.PrincipalId,
            ClientMessageId = command.ClientMessageId,
            Sender = command.Sender,
            Recipient = destination,
            Content = command.Content,
            RootMessageId = messageId,
            HopCount = 0,
            AttachmentsJson = JsonSerializer.Serialize(command.Attachments ?? []),
            CreatedAt = now,
        };
        var run = new RunRecord
        {
            Id = runId,
            ChannelId = command.ChannelId,
            InputMessageId = messageId,
            State = RunState.Queued,
            AcceptedAt = now,
            Adapter = adapter,
        };
        var outboxEvent = new OutboxEventRecord
        {
            Id = eventId,
            EventName = "run.queued",
            AggregateId = runId,
            Payload = JsonSerializer.Serialize(new { eventId, runId, channelId = command.ChannelId }),
            OccurredAt = now,
        };

        context.Messages.Add(message);
        context.Runs.Add(run);
        context.OutboxEvents.Add(outboxEvent);
        context.AuditEvents.Add(new AuditRecord
        {
            Id = $"aud_{Guid.NewGuid():N}", EventName = "route.selected", ChannelId = command.ChannelId,
            MessageId = messageId, RunId = runId, EventId = eventId, OccurredAt = now,
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException) when (string.Equals(channel.Ordering, "serial", StringComparison.OrdinalIgnoreCase))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw new DomainException("A serial channel already has an active run.");
        }
        catch (DbUpdateException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            await using var lookupContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var concurrentReplay = await FindReplayAsync(lookupContext, command, cancellationToken);
            if (concurrentReplay is not null)
            {
                return concurrentReplay;
            }

            if (string.Equals(channel.Ordering, "serial", StringComparison.OrdinalIgnoreCase)
                && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new DomainException("A serial channel already has an active run.");
            }

            throw;
        }

        HubTelemetry.MessagesAccepted.Add(1);

        return new AcceptMessageResult(messageId, runId, message.Sequence, false);
    }

    private static async Task<AcceptMessageResult?> FindReplayAsync(
        HubDbContext context,
        AcceptMessageCommand command,
        CancellationToken cancellationToken)
    {
        var message = await context.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == command.PrincipalId && item.ClientMessageId == command.ClientMessageId,
                cancellationToken);
        if (message is null)
        {
            return null;
        }

        var run = await context.Runs
            .AsNoTracking()
            .SingleAsync(item => item.InputMessageId == message.Id, cancellationToken);
        return new AcceptMessageResult(message.Id, run.Id, message.Sequence, true);
    }

    private async Task RecordQuotaRejectionAsync(string channelId, string eventName, CancellationToken cancellationToken)
    {
        await using var auditContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        auditContext.AuditEvents.Add(new AuditRecord
        {
            Id = $"aud_{Guid.NewGuid():N}", EventName = eventName, ChannelId = channelId, OccurredAt = DateTimeOffset.UtcNow,
        });
        await auditContext.SaveChangesAsync(cancellationToken);
    }
}
