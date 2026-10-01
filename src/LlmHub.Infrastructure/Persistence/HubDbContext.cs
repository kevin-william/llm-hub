using LlmHub.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Persistence;

public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<ChannelRecord> Channels => Set<ChannelRecord>();

    public DbSet<ChannelParticipantRecord> ChannelParticipants => Set<ChannelParticipantRecord>();

    public DbSet<EndpointRecord> Endpoints => Set<EndpointRecord>();

    public DbSet<MessageRecord> Messages => Set<MessageRecord>();

    public DbSet<RunRecord> Runs => Set<RunRecord>();

    public DbSet<RunAttemptRecord> RunAttempts => Set<RunAttemptRecord>();

    public DbSet<AgentSessionRecord> AgentSessions => Set<AgentSessionRecord>();

    public DbSet<SubscriptionRecord> Subscriptions => Set<SubscriptionRecord>();

    public DbSet<OutboxEventRecord> OutboxEvents => Set<OutboxEventRecord>();

    public DbSet<WebhookDeliveryRecord> WebhookDeliveries => Set<WebhookDeliveryRecord>();

    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();

    public DbSet<WorkerRecord> Workers => Set<WorkerRecord>();

    public DbSet<AuditRecord> AuditEvents => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChannelRecord>(entity =>
        {
            entity.ToTable("channels");
            entity.HasKey(channel => channel.Id);
            entity.Property(channel => channel.Id).HasMaxLength(80);
            entity.Property(channel => channel.Ordering).HasMaxLength(16);
            entity.Property(channel => channel.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<ChannelParticipantRecord>(entity =>
        {
            entity.ToTable("channel_participants");
            entity.HasKey(participant => new { participant.ChannelId, participant.PrincipalId });
            entity.Property(participant => participant.ChannelId).HasMaxLength(80);
            entity.Property(participant => participant.PrincipalId).HasMaxLength(160);
        });

        modelBuilder.Entity<EndpointRecord>(entity =>
        {
            entity.ToTable("endpoints");
            entity.HasKey(endpoint => endpoint.Id);
            entity.HasIndex(endpoint => endpoint.Address).IsUnique();
            entity.Property(endpoint => endpoint.CapabilitiesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MessageRecord>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.PrincipalId, message.ClientMessageId }).IsUnique();
            entity.HasIndex(message => new { message.ChannelId, message.Sequence }).IsUnique();
            entity.Property(message => message.AttachmentsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.ToTable("runs");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => new { run.ChannelId, run.State });
            entity.Property(run => run.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(run => run.FailureKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(run => run.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<RunAttemptRecord>(entity =>
        {
            entity.ToTable("run_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => new { attempt.RunId, attempt.Number }).IsUnique();
        });

        modelBuilder.Entity<AgentSessionRecord>(entity =>
        {
            entity.ToTable("agent_sessions");
            entity.HasKey(session => session.Id);
            entity.HasIndex(session => new { session.ChannelId, session.Adapter }).IsUnique();
        });

        modelBuilder.Entity<SubscriptionRecord>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.HasIndex(subscription => new { subscription.PrincipalId, subscription.ChannelId, subscription.EventName });
        });

        modelBuilder.Entity<OutboxEventRecord>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(outboxEvent => outboxEvent.Id);
            entity.HasIndex(outboxEvent => new { outboxEvent.PublishedAt, outboxEvent.OccurredAt });
            entity.Property(outboxEvent => outboxEvent.Payload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<WebhookDeliveryRecord>(entity =>
        {
            entity.ToTable("webhook_deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new { delivery.EventId, delivery.SubscriptionId, delivery.Attempt }).IsUnique();
        });

        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.ToTable("artifacts");
            entity.HasKey(artifact => artifact.Id);
            entity.HasIndex(artifact => new { artifact.RunId, artifact.ContentHash }).IsUnique();
        });

        modelBuilder.Entity<WorkerRecord>(entity =>
        {
            entity.ToTable("workers");
            entity.HasKey(worker => worker.Id);
            entity.Property(worker => worker.CapabilitiesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => new { audit.RunId, audit.OccurredAt });
            entity.HasIndex(audit => new { audit.DeliveryId, audit.OccurredAt });
        });
    }
}

public sealed class ChannelRecord
{
    public string Id { get; set; } = null!;

    public string Ordering { get; set; } = "serial";

    public int MaxHops { get; set; } = 8;

    public long LastSequence { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChannelParticipantRecord
{
    public string ChannelId { get; set; } = null!;

    public string PrincipalId { get; set; } = null!;
}

public sealed class EndpointRecord
{
    public string Id { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string Adapter { get; set; } = null!;

    public string CapabilitiesJson { get; set; } = "[]";

    public string Status { get; set; } = "offline";
}

public sealed class MessageRecord
{
    public string Id { get; set; } = null!;

    public string ChannelId { get; set; } = null!;

    public long Sequence { get; set; }

    public string PrincipalId { get; set; } = null!;

    public string ClientMessageId { get; set; } = null!;

    public string Sender { get; set; } = null!;

    public string Recipient { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string RootMessageId { get; set; } = null!;

    public string? ReplyToMessageId { get; set; }

    public string? CausationId { get; set; }

    public int HopCount { get; set; }

    public string AttachmentsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RunRecord
{
    public string Id { get; set; } = null!;

    public string ChannelId { get; set; } = null!;

    public string InputMessageId { get; set; } = null!;

    public RunState State { get; set; }

    public FailureKind? FailureKind { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTimeOffset AcceptedAt { get; set; }

    public string Adapter { get; set; } = "opencode";

    public string? WorkerId { get; set; }

    public string? LeaseToken { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public bool CancelRequested { get; set; }

    public DateTimeOffset? Deadline { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? RetryAt { get; set; }
}

public sealed class RunAttemptRecord
{
    public string Id { get; set; } = null!;

    public string RunId { get; set; } = null!;

    public int Number { get; set; }

    public string? LeaseToken { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }
}

public sealed class AgentSessionRecord
{
    public string Id { get; set; } = null!;

    public string ChannelId { get; set; } = null!;

    public string Adapter { get; set; } = null!;

    public string ExternalSessionId { get; set; } = null!;
}

public sealed class SubscriptionRecord
{
    public string Id { get; set; } = null!;

    public string PrincipalId { get; set; } = null!;

    public string ChannelId { get; set; } = null!;

    public string EventName { get; set; } = null!;

    public string CallbackUrl { get; set; } = null!;

    public string EncryptedSecret { get; set; } = null!;

    public string? Cursor { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsVerified { get; set; }
}

public sealed class OutboxEventRecord
{
    public string Id { get; set; } = null!;

    public string EventName { get; set; } = null!;

    public string AggregateId { get; set; } = null!;

    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class WebhookDeliveryRecord
{
    public string Id { get; set; } = null!;

    public string EventId { get; set; } = null!;

    public string SubscriptionId { get; set; } = null!;

    public int Attempt { get; set; }

    public string CallbackUrl { get; set; } = null!;

    public string Payload { get; set; } = "{}";

    public string SignatureTimestamp { get; set; } = null!;

    public string State { get; set; } = "pending";

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class ArtifactRecord
{
    public string Id { get; set; } = null!;

    public string ContentHash { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long Length { get; set; }

    public string RunId { get; set; } = null!;

    public string ChannelId { get; set; } = null!;
}

public sealed class WorkerRecord
{
    public string Id { get; set; } = null!;

    public string Adapter { get; set; } = null!;

    public string CapabilitiesJson { get; set; } = "[]";

    public string Version { get; set; } = null!;

    public int MaxConcurrency { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
}

public sealed class AuditRecord
{
    public string Id { get; set; } = null!;
    public string EventName { get; set; } = null!;
    public string? ChannelId { get; set; }
    public string? MessageId { get; set; }
    public string? RunId { get; set; }
    public string? EventId { get; set; }
    public string? DeliveryId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
