using LlmHub.Contracts.Events;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Security;

public sealed class WebhookSubscriptionVerificationTests
{
    [Fact]
    public async Task CallbackThatRejectsChallengeDoesNotCreateSubscription()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"subscription-verification-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var service = new PostgresSubscriptionService(
            factory,
            new AesGcmWebhookSecretProtector("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            new RejectingVerifier());

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(
            "principal:verification", "tenant:development", new CreateSubscriptionRequest("chn_1", "message.created", "https://callback.example.test/events", "whsec_test", 300), CancellationToken.None));
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Empty(context.Subscriptions);
    }

    [Fact]
    public async Task CallbackQuotaRejectsAnotherActiveSubscriptionWithoutCallingTheVerifier()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"subscription-quota-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Channels.Add(new ChannelRecord { Id = "chn_1", CreatedAt = DateTimeOffset.UtcNow });
            seed.ChannelParticipants.Add(new ChannelParticipantRecord { ChannelId = "chn_1", PrincipalId = "principal:quota" });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var verifier = new CountingVerifier();
        var service = new PostgresSubscriptionService(
            factory,
            new AesGcmWebhookSecretProtector("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            verifier,
            new LlmHub.Application.Messaging.HubQuotaOptions(MaxActiveSubscriptionsPerPrincipal: 1));
        var first = new CreateSubscriptionRequest("chn_1", "message.created", "https://callback.example.test/first", "whsec_first", 300);
        var second = new CreateSubscriptionRequest("chn_1", "message.created", "https://callback.example.test/second", "whsec_second", 300);

        await service.CreateAsync("principal:quota", "tenant:development", first, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync("principal:quota", "tenant:development", second, CancellationToken.None));

        Assert.Equal("CALLBACK_QUOTA_EXCEEDED", exception.Message);
        Assert.Equal(1, verifier.CallCount);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Contains(verify.AuditEvents, audit => audit.EventName == "quota.callbacks.rejected");
    }

    private sealed class RejectingVerifier : IWebhookVerificationClient
    {
        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class CountingVerifier : IWebhookVerificationClient
    {
        public int CallCount { get; private set; }
        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
