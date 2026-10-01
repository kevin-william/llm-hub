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
            "principal:verification", new CreateSubscriptionRequest("chn_1", "message.created", "https://callback.example.test/events", "whsec_test", 300), CancellationToken.None));
        await using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Empty(context.Subscriptions);
    }

    private sealed class RejectingVerifier : IWebhookVerificationClient
    {
        public Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
