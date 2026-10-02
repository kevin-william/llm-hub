using System.Security.Claims;
using LlmHub.Api.Authentication;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Security;

public sealed class WorkerIdentityValidatorTests
{
    [Fact]
    public async Task AuthenticatedWorkerCanAccessOnlyItsOwnRegistrationAndRun()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"worker-identity-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = "chn_1", TenantId = "tenant:oidc/tenant_1", CreatedAt = DateTimeOffset.UtcNow });
            seed.Runs.AddRange(
                new RunRecord { Id = "run_owned", ChannelId = "chn_1", InputMessageId = "msg_1", WorkerId = "worker_1", State = RunState.Running, AcceptedAt = DateTimeOffset.UtcNow },
                new RunRecord { Id = "run_other", ChannelId = "chn_1", InputMessageId = "msg_2", WorkerId = "worker_2", State = RunState.Running, AcceptedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var worker = new ClaimsPrincipal(new ClaimsIdentity([new Claim("worker_id", "worker_1"), new Claim("tid", "tenant_1")], "test"));
        var validator = new WorkerIdentityValidator(factory);

        Assert.True(WorkerIdentityValidator.CanAccessWorker(worker, "worker_1"));
        Assert.False(WorkerIdentityValidator.CanAccessWorker(worker, "worker_2"));
        Assert.True(await validator.CanAccessRunAsync(worker, "run_owned", CancellationToken.None));
        Assert.False(await validator.CanAccessRunAsync(worker, "run_other", CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutWorkerClaimCannotAccessWorkerRun()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"worker-identity-{Guid.NewGuid():N}").Options;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "tenant_1")], "test"));
        var validator = new WorkerIdentityValidator(new TestDbContextFactory(options));

        Assert.False(WorkerIdentityValidator.CanAccessWorker(principal, "worker_1"));
        Assert.False(await validator.CanAccessRunAsync(principal, "run_missing", CancellationToken.None));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
