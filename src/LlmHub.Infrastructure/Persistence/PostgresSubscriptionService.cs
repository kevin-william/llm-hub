using LlmHub.Application.Events;
using LlmHub.Contracts.Events;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresSubscriptionService(
    IDbContextFactory<HubDbContext> contextFactory,
    IWebhookSecretProtector secretProtector,
    IWebhookVerificationClient verificationClient) : ISubscriptionService
{
    public async Task<SubscriptionResponse> CreateAsync(string principalId, CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttps)
        {
            throw new DomainException("The callback URL must use HTTPS.");
        }

        CallbackPolicy.ValidateUri(callback);

        if (request.DurationSeconds is < 1 or > 86_400 || string.IsNullOrWhiteSpace(request.Secret))
        {
            throw new DomainException("A secret and a duration between 1 and 86400 seconds are required.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var isParticipant = await context.ChannelParticipants.AnyAsync(
            participant => participant.ChannelId == request.ChannelId && participant.PrincipalId == principalId,
            cancellationToken);
        if (!isParticipant)
        {
            throw new DomainException("CHANNEL_ACCESS_DENIED");
        }

        var id = $"sub_{Guid.NewGuid():N}";
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(request.DurationSeconds);
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        if (!await verificationClient.VerifyAsync(callback, challenge, cancellationToken))
        {
            throw new DomainException("The callback did not complete webhook verification.");
        }

        context.Subscriptions.Add(new SubscriptionRecord
        {
            Id = id,
            PrincipalId = principalId,
            ChannelId = request.ChannelId,
            EventName = request.EventName,
            CallbackUrl = callback.AbsoluteUri,
            EncryptedSecret = secretProtector.Protect(request.Secret),
            ExpiresAt = expiresAt,
            IsVerified = true,
        });
        await context.SaveChangesAsync(cancellationToken);
        return new SubscriptionResponse(id, request.ChannelId, request.EventName, expiresAt, true);
    }

    public async Task RemoveAsync(string principalId, string subscriptionId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subscription = await context.Subscriptions.SingleOrDefaultAsync(
            item => item.Id == subscriptionId && item.PrincipalId == principalId,
            cancellationToken);
        if (subscription is not null)
        {
            context.Subscriptions.Remove(subscription);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
