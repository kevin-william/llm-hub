using LlmHub.Contracts.Workers;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Workers;

public sealed class PostgresWorkerGatewayIntegrationTests
{
    [Fact]
    public async Task ConcurrentWorkersClaimTheQueuedRunOnlyOnce()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only")
            .Options;
        var factory = new TestDbContextFactory(options);
        var suffix = Guid.NewGuid().ToString("N");
        var channelId = $"chn_pg_claim_{suffix}";
        var messageId = $"msg_pg_claim_{suffix}";
        var runId = $"run_pg_claim_{suffix}";
        var adapter = $"adapter-{suffix}";
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = channelId, CreatedAt = DateTimeOffset.UtcNow });
            seed.Messages.Add(new MessageRecord
            {
                Id = messageId, ChannelId = channelId, Sequence = 1, PrincipalId = "principal:worker-test",
                ClientMessageId = $"client_{suffix}", Sender = "principal:worker-test", Recipient = "agent:opencode/default",
                Content = "claim once", RootMessageId = messageId, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.Runs.Add(new RunRecord
            {
                Id = runId, ChannelId = channelId, InputMessageId = messageId, State = RunState.Queued,
                AcceptedAt = DateTimeOffset.UtcNow, Adapter = adapter,
            });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var firstGateway = new PostgresWorkerGateway(factory);
        var secondGateway = new PostgresWorkerGateway(factory);
        await firstGateway.RegisterAsync(new RegisterWorkerRequest($"worker_a_{suffix}", adapter, [], "test", 1), CancellationToken.None);
        await secondGateway.RegisterAsync(new RegisterWorkerRequest($"worker_b_{suffix}", adapter, [], "test", 1), CancellationToken.None);

        var claims = await Task.WhenAll(
            firstGateway.ClaimAsync($"worker_a_{suffix}", CancellationToken.None),
            secondGateway.ClaimAsync($"worker_b_{suffix}", CancellationToken.None));
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        var claim = Assert.Single(claims, claim => claim is not null);
        Assert.Equal(runId, claim!.RunId);
        Assert.Single(await verify.RunAttempts.Where(attempt => attempt.RunId == runId).ToListAsync(CancellationToken.None));
        Assert.Equal(RunState.Leased, (await verify.Runs.SingleAsync(run => run.Id == runId, CancellationToken.None)).State);
        Assert.Equal(2, await verify.Endpoints.CountAsync(endpoint => endpoint.Adapter == adapter && endpoint.Status == "online"));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
