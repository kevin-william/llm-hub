using LlmHub.Api.Authentication;
using LlmHub.Application.Channels;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Workers;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LlmHub.IntegrationTests.Security;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task SamePrincipalCannotSendToAChannelFromAnotherTenant()
    {
        var factory = CreateFactory();
        var channel = await new PostgresChannelService(factory).CreateAsync(
            new CreateChannelCommand("principal:shared", "agent:echo/default", null, null, TenantId: "tenant:one"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainException>(() => new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand(channel.ChannelId, "principal:shared", "principal:shared", "agent:echo/default", "tenant-test", "private", false, TenantId: "tenant:two"),
            CancellationToken.None));

        Assert.Equal("CHANNEL_ACCESS_DENIED", exception.Message);
    }

    [Fact]
    public async Task WorkerCannotClaimRunsFromAnotherTenant()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = "chn_tenant", TenantId = "tenant:two", CreatedAt = DateTimeOffset.UtcNow });
            seed.Messages.Add(new MessageRecord
            {
                Id = "msg_tenant", ChannelId = "chn_tenant", Sequence = 1, PrincipalId = "principal:user", ClientMessageId = "tenant-input",
                Sender = "principal:user", Recipient = "agent:echo/default", Content = "work", RootMessageId = "msg_tenant", CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.Runs.Add(new RunRecord { Id = "run_tenant", ChannelId = "chn_tenant", InputMessageId = "msg_tenant", State = RunState.Queued, Adapter = "echo", AcceptedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var gateway = new PostgresWorkerGateway(factory);
        await gateway.RegisterAsync(new RegisterWorkerRequest("worker_tenant_one", "echo", [], "test", 1), CancellationToken.None, "tenant:one");

        Assert.Null(await gateway.ClaimAsync("worker_tenant_one", CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticatedRequestsRequireATenantClaim()
    {
        var middleware = new TenantAuthorizationMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var missingTenant = Context([]);
        var tenant = Context([new Claim("tid", "tenant_1")]);

        await middleware.InvokeAsync(missingTenant);
        await middleware.InvokeAsync(tenant);

        Assert.Equal(StatusCodes.Status403Forbidden, missingTenant.Response.StatusCode);
        Assert.Equal(StatusCodes.Status204NoContent, tenant.Response.StatusCode);
    }

    private static DefaultHttpContext Context(Claim[] claims)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/channels";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"tenant-isolation-{Guid.NewGuid():N}").Options;
        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
