using LlmHub.Application.Messaging;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Observability;

public sealed class AuditTrailTests
{
    [Fact]
    public async Task RouteAuditPreservesCorrelationWithoutRecordingPromptOrSecret()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"audit-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = "chn_audit", CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = "chn_audit", PrincipalId = "principal:audit" });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var result = await new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand("chn_audit", "principal:audit", "principal:audit", "agent:opencode/default", "audit-client", "prompt-secret-value", false),
            CancellationToken.None);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);
        var audit = await verify.AuditEvents.SingleAsync(CancellationToken.None);

        Assert.Equal("route.selected", audit.EventName);
        Assert.Equal("chn_audit", audit.ChannelId);
        Assert.Equal(result.MessageId, audit.MessageId);
        Assert.Equal(result.RunId, audit.RunId);
        Assert.DoesNotContain("prompt-secret-value", string.Join('|', audit.EventName, audit.ChannelId, audit.MessageId, audit.RunId, audit.EventId));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
