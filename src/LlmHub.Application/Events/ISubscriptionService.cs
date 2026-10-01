using LlmHub.Contracts.Events;

namespace LlmHub.Application.Events;

public interface ISubscriptionService
{
    Task<SubscriptionResponse> CreateAsync(string principalId, CreateSubscriptionRequest request, CancellationToken cancellationToken);

    Task RemoveAsync(string principalId, string subscriptionId, CancellationToken cancellationToken);
}
