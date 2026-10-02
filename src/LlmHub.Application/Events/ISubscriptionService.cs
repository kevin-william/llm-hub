using LlmHub.Contracts.Events;

namespace LlmHub.Application.Events;

public interface ISubscriptionService
{
    Task<SubscriptionResponse> CreateAsync(string principalId, string tenantId, CreateSubscriptionRequest request, CancellationToken cancellationToken);

    Task RemoveAsync(string principalId, string tenantId, string subscriptionId, CancellationToken cancellationToken);
}
