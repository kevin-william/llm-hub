using LlmHub.Application.Events;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Events;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresSubscriptionService(
    IDbContextFactory<HubDbContext> contextFactory,
    IWebhookSecretProtector secretProtector,
    IWebhookVerificationClient verificationClient,
    HubQuotaOptions? quotas = null) : ISubscriptionService
{
    public async Task<SubscriptionResponse> CreateAsync(string principalId, string tenantId, CreateSubscriptionRequest request, CancellationToken cancellationToken)
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
        var isTenantChannel = await context.Channels.AnyAsync(
            channel => channel.Id == request.ChannelId && channel.TenantId == tenantId,
            cancellationToken);
        if (!isParticipant || !isTenantChannel)
        {
            throw new DomainException("CHANNEL_ACCESS_DENIED");
        }

        var activeSubscriptions = await context.Subscriptions.CountAsync(
            subscription => subscription.PrincipalId == principalId && subscription.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (activeSubscriptions >= (quotas ?? HubQuotaOptions.Default).MaxActiveSubscriptionsPerPrincipal)
        {
            context.AuditEvents.Add(new AuditRecord
            {
                Id = $"aud_{Guid.NewGuid():N}", EventName = "quota.callbacks.rejected", ChannelId = request.ChannelId,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
            throw new DomainException("CALLBACK_QUOTA_EXCEEDED");
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

    public async Task RemoveAsync(string principalId, string tenantId, string subscriptionId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subscription = await context.Subscriptions.SingleOrDefaultAsync(
            item => item.Id == subscriptionId && item.PrincipalId == principalId,
            cancellationToken);
        if (subscription is not null && await context.Channels.AnyAsync(channel => channel.Id == subscription.ChannelId && channel.TenantId == tenantId, cancellationToken))
        {
            context.Subscriptions.Remove(subscription);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
