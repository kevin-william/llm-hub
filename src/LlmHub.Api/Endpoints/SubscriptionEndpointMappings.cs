using LlmHub.Application.Events;
using LlmHub.Contracts.Events;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Webhooks;

namespace LlmHub.Api.Endpoints;

public static class SubscriptionEndpointMappings
{
    public static void MapSubscriptionEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/subscriptions", CreateAsync);
        app.MapDelete("/v1/subscriptions/{subscriptionId}", RemoveAsync);
    }

    private static async Task<IResult> CreateAsync(CreateSubscriptionRequest request, HttpRequest httpRequest, ISubscriptionService service, CancellationToken cancellationToken)
    {
        try
        {
            var principal = httpRequest.Headers.TryGetValue("X-Principal-Id", out var header) ? header.ToString() : "principal:development";
            return Results.Created($"/v1/subscriptions", await service.CreateAsync(principal, request, cancellationToken));
        }
        catch (DomainException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (WebhookSecretProtectionException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> RemoveAsync(string subscriptionId, HttpRequest httpRequest, ISubscriptionService service, CancellationToken cancellationToken)
    {
        var principal = httpRequest.Headers.TryGetValue("X-Principal-Id", out var header) ? header.ToString() : "principal:development";
        await service.RemoveAsync(principal, subscriptionId, cancellationToken);
        return Results.NoContent();
    }
}
