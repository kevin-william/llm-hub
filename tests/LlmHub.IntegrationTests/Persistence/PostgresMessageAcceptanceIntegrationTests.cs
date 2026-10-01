using LlmHub.Application.Messaging;
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

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
