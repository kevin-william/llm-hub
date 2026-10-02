using LlmHub.Application.Messaging;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Persistence;

public sealed class PostgresMessageAcceptanceIntegrationTests
{
    [Fact]
    public async Task MessageReplayUsesTheSamePersistentMessageRunAndOutboxEvent()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only")
            .Options;
        var factory = new TestDbContextFactory(options);
        var suffix = Guid.NewGuid().ToString("N");
        var channelId = $"chn_pg_{suffix}";
        var principal = $"principal:test/{suffix}";
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = channelId, CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = channelId, PrincipalId = principal });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var service = new PostgresMessageAcceptanceService(factory);
        var command = new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", $"client_{suffix}", "Durable message", false);
        var first = await service.AcceptAsync(command, CancellationToken.None);
        var replay = await service.AcceptAsync(command, CancellationToken.None);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.MessageId, replay.MessageId);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Single(await verify.Messages.Where(item => item.ChannelId == channelId).ToListAsync(CancellationToken.None));
        Assert.Single(await verify.Runs.Where(item => item.ChannelId == channelId).ToListAsync(CancellationToken.None));
        Assert.Single(await verify.OutboxEvents.Where(item => item.AggregateId == first.RunId).ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentReplaysProduceOnePersistentMessageRunAndOutboxEvent()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only")
            .Options;
        var factory = new TestDbContextFactory(options);
        var suffix = Guid.NewGuid().ToString("N");
        var channelId = $"chn_pg_concurrent_{suffix}";
        var principal = $"principal:test/{suffix}";
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = channelId, CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = channelId, PrincipalId = principal });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var command = new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", $"client_{suffix}", "Durable message", false);
        var results = await Task.WhenAll(
            new PostgresMessageAcceptanceService(factory).AcceptAsync(command, CancellationToken.None),
            new PostgresMessageAcceptanceService(factory).AcceptAsync(command, CancellationToken.None));
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Equal(results[0].MessageId, results[1].MessageId);
        Assert.Equal(results[0].RunId, results[1].RunId);
        Assert.Single(await verify.Messages.Where(item => item.ChannelId == channelId).ToListAsync(CancellationToken.None));
        Assert.Single(await verify.Runs.Where(item => item.ChannelId == channelId).ToListAsync(CancellationToken.None));
        Assert.Single(await verify.OutboxEvents.Where(item => item.AggregateId == results[0].RunId).ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentDistinctMessagesLeaveOnlyOneActiveRunInASerialChannel()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only")
            .Options;
        var factory = new TestDbContextFactory(options);
        var suffix = Guid.NewGuid().ToString("N");
        var channelId = $"chn_pg_serial_{suffix}";
        var principal = $"principal:test/{suffix}";
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = channelId, Ordering = "serial", CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = channelId, PrincipalId = principal });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var accepted = new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", $"client_a_{suffix}", "first", false),
            CancellationToken.None);
        var rejected = new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", $"client_b_{suffix}", "second", false),
            CancellationToken.None);
        var outcomes = await Task.WhenAll(ObserveAsync(accepted), ObserveAsync(rejected));
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DomainException { Message: "A serial channel already has an active run." });
        Assert.Single(await verify.Runs.Where(item => item.ChannelId == channelId).ToListAsync(CancellationToken.None));
    }

    private static async Task<Exception?> ObserveAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
