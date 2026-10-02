using LlmHub.Application.Messaging;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Persistence;

public sealed class MessageAcceptanceTests
{
    [Fact]
    public async Task SameClientMessageIdCreatesOneLogicalMessageRunAndOutboxEvent()
    {
        var factory = CreateFactory();
        await SeedChannelAsync(factory);
        var service = new PostgresMessageAcceptanceService(factory);
        var command = new AcceptMessageCommand(
            "chn_1",
            "principal:chatgpt/user-1",
            "principal:chatgpt/user-1",
            "agent:opencode/default",
            "client_1",
            "Analyze the repository.",
            false);

        var first = await service.AcceptAsync(command, CancellationToken.None);
        var replay = await service.AcceptAsync(command, CancellationToken.None);
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.MessageId, replay.MessageId);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Single(await context.Messages.ToListAsync(CancellationToken.None));
        Assert.Single(await context.Runs.ToListAsync(CancellationToken.None));
        Assert.Single(await context.OutboxEvents.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AwaitResponseRequiresVerifiedActiveSubscription()
    {
        var factory = CreateFactory();
        await SeedChannelAsync(factory);
        var service = new PostgresMessageAcceptanceService(factory);

        await Assert.ThrowsAsync<DomainException>(() => service.AcceptAsync(
            new AcceptMessageCommand(
                "chn_1",
                "principal:chatgpt/user-1",
                "principal:chatgpt/user-1",
                "agent:opencode/default",
                "client_1",
                "Analyze the repository.",
                true),
            CancellationToken.None));
    }

    [Fact]
    public async Task MessageQuotaRejectsOversizedContentBeforeCreatingRun()
    {
        var factory = CreateFactory();
        await SeedChannelAsync(factory);
        var service = new PostgresMessageAcceptanceService(factory, new HubQuotaOptions(MaxMessageBytes: 3));

        await Assert.ThrowsAsync<DomainException>(() => service.AcceptAsync(
            new AcceptMessageCommand("chn_1", "principal:chatgpt/user-1", "principal:chatgpt/user-1", "agent:opencode/default", "too-large", "four", false),
            CancellationToken.None));
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Empty(context.Messages);
        Assert.Empty(context.Runs);
        Assert.Contains(context.AuditEvents, audit => audit.EventName == "quota.message.rejected");
    }

    [Fact]
    public async Task ChannelQuotaRejectsNewChannelsForTheSamePrincipal()
    {
        var factory = CreateFactory();
        var service = new PostgresChannelService(factory, new HubQuotaOptions(MaxChannelsPerPrincipal: 1));

        await service.CreateAsync(new LlmHub.Application.Channels.CreateChannelCommand(
            "principal:chatgpt/user-1", "agent:opencode/default", null, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(
            new LlmHub.Application.Channels.CreateChannelCommand("principal:chatgpt/user-1", "agent:echo/default", null, null), CancellationToken.None));

        Assert.Equal("CHANNEL_QUOTA_EXCEEDED", exception.Message);
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Contains(context.AuditEvents, audit => audit.EventName == "quota.channels.rejected");
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase($"llm-hub-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task SeedChannelAsync(TestDbContextFactory factory)
    {
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        context.Channels.Add(new ChannelRecord { Id = "chn_1", CreatedAt = DateTimeOffset.UtcNow });
        context.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = "chn_1", PrincipalId = "principal:chatgpt/user-1" });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
