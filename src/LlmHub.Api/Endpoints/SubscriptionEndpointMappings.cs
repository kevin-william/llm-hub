using LlmHub.Application.Events;
using LlmHub.Api.Authentication;
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
            return Results.Created($"/v1/subscriptions", await service.CreateAsync(RequestIdentity.Principal(httpRequest.HttpContext), RequestIdentity.Tenant(httpRequest.HttpContext), request, cancellationToken));
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
        await service.RemoveAsync(RequestIdentity.Principal(httpRequest.HttpContext), RequestIdentity.Tenant(httpRequest.HttpContext), subscriptionId, cancellationToken);
        return Results.NoContent();
    }
}
