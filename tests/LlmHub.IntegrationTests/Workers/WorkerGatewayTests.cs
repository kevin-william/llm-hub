using LlmHub.Contracts.Artifacts;
using LlmHub.Contracts.Workers;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Workers;

public sealed class WorkerGatewayTests
{
    [Fact]
    public async Task WorkerClaimsHeartbeatsAndCompletesWithCurrentLease()
    {
        var factory = CreateFactory();
        await SeedRunAsync(factory, "run_1");
        var gateway = new PostgresWorkerGateway(factory);
        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_1", "opencode", [], "1.0.0", 1), CancellationToken.None);

        var claim = await gateway.ClaimAsync("worker_1", CancellationToken.None);
        var heartbeat = await gateway.HeartbeatAsync(claim!.RunId, new HeartbeatRequest(claim.LeaseToken), CancellationToken.None);
        await gateway.CompleteAsync(claim.RunId, new CompleteRunRequest(claim.LeaseToken), CancellationToken.None);
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.False(heartbeat.CancelRequested);
        Assert.True(heartbeat.LeaseExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(RunState.Succeeded, (await context.Runs.SingleAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task OldLeaseCannotCompleteRunAfterReassignment()
    {
        var factory = CreateFactory();
        await SeedRunAsync(factory, "run_1");
        var gateway = new PostgresWorkerGateway(factory);
        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_1", "opencode", [], "1.0.0", 1), CancellationToken.None);
        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_2", "opencode", [], "1.0.0", 1), CancellationToken.None);
        var first = await gateway.ClaimAsync("worker_1", CancellationToken.None);
        await using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var run = await context.Runs.SingleAsync(CancellationToken.None);
            run.State = RunState.Queued;
            run.LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            run.LeaseToken = null;
            run.WorkerId = null;
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var second = await gateway.ClaimAsync("worker_2", CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(() => gateway.CompleteAsync(first!.RunId, new CompleteRunRequest(first.LeaseToken), CancellationToken.None));
        await gateway.CompleteAsync(second!.RunId, new CompleteRunRequest(second.LeaseToken), CancellationToken.None);
    }

    [Fact]
    public async Task CompletionPersistsArtifactsAndSessionWithoutDuplicatingOnReplay()
    {
        var factory = CreateFactory();
        await SeedRunAsync(factory, "run_1");
        var gateway = new PostgresWorkerGateway(factory);
        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_1", "opencode", [], "1.0.0", 1), CancellationToken.None);
        var claim = (await gateway.ClaimAsync("worker_1", CancellationToken.None))!;
        await gateway.HeartbeatAsync(claim.RunId, new HeartbeatRequest(claim.LeaseToken), CancellationToken.None);
        var artifact = new ArtifactReference($"sha256/{new string('a', 64)}", new string('a', 64), "text/plain", 8);
        var completion = new CompleteRunRequest(claim.LeaseToken, "done", "opencode-session-1", [artifact]);

        await gateway.CompleteAsync(claim.RunId, completion, CancellationToken.None);
        await gateway.CompleteAsync(claim.RunId, completion, CancellationToken.None);
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        var storedArtifact = await context.Artifacts.SingleAsync(CancellationToken.None);
        Assert.Equal("run_1", storedArtifact.RunId);
        Assert.Equal("chn_1", storedArtifact.ChannelId);
        Assert.Equal("opencode-session-1", (await context.AgentSessions.SingleAsync(CancellationToken.None)).ExternalSessionId);
        Assert.Single(await context.Messages.Where(message => message.CausationId == "msg_1").ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredLeaseMovesToRetryWaitAndCanBeRequeued()
    {
        var factory = CreateFactory();
        await SeedRunAsync(factory, "run_1");
        await using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var run = await context.Runs.SingleAsync(CancellationToken.None);
            run.State = RunState.Running;
            run.LeaseToken = "expired";
            run.LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var recovery = new RunRecoveryService(factory);
        Assert.Equal(1, await recovery.RecoverExpiredLeasesAsync(CancellationToken.None));
        await using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var run = await context.Runs.SingleAsync(CancellationToken.None);
            Assert.Equal(RunState.RetryWait, run.State);
            run.RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(1, await recovery.EnqueueReadyRetriesAsync(CancellationToken.None));
        await using var finalContext = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(RunState.Queued, (await finalContext.Runs.SingleAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task ThirdExpiredLeaseMovesRunToDeadLetter()
    {
        var factory = CreateFactory();
        await SeedRunAsync(factory, "run_1");
        await using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var run = await context.Runs.SingleAsync(CancellationToken.None);
            run.State = RunState.Running;
            run.AttemptCount = 2;
            run.LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var recovery = new RunRecoveryService(factory);
        await recovery.RecoverExpiredLeasesAsync(CancellationToken.None);
        await using var finalContext = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Equal(RunState.DeadLetter, (await finalContext.Runs.SingleAsync(CancellationToken.None)).State);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase($"workers-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task SeedRunAsync(TestDbContextFactory factory, string runId)
    {
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        context.Channels.Add(new ChannelRecord { Id = "chn_1", CreatedAt = DateTimeOffset.UtcNow });
        context.Messages.Add(new MessageRecord
        {
            Id = "msg_1",
            ChannelId = "chn_1",
            Sequence = 1,
            PrincipalId = "principal:user",
            ClientMessageId = "client_1",
            Sender = "principal:user",
            Recipient = "agent:opencode/default",
            Content = "input",
            RootMessageId = "msg_1",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Runs.Add(new RunRecord
        {
            Id = runId,
            ChannelId = "chn_1",
            InputMessageId = "msg_1",
            State = RunState.Queued,
            AcceptedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
