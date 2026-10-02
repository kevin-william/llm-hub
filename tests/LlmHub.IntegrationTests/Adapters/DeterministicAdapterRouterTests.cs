using LlmHub.Application.Messaging;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Adapters;

public sealed class DeterministicAdapterRouterTests
{
    [Fact]
    public async Task EchoDestinationCreatesRunForEchoAdapter()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"router-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = "chn_echo", Destination = "agent:echo/default", CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = "chn_echo", PrincipalId = "principal:router" });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var accepted = await new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand("chn_echo", "principal:router", "principal:router", "agent:opencode/default", "echo-client", "hello", false),
            CancellationToken.None);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Equal("echo", (await verify.Runs.SingleAsync(run => run.Id == accepted.RunId)).Adapter);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
