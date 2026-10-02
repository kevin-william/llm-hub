using System.Security.Claims;

namespace LlmHub.Api.Authentication;

public sealed class ScopeAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context);
            return;
        }

        var requiredScope = RequiredScope(context.Request);
        if (requiredScope is not null && !HasScope(context.User, requiredScope))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private static string? RequiredScope(HttpRequest request)
    {
        if (request.Method == HttpMethods.Get)
        {
            return "hub.read";
        }

        if (request.Method == HttpMethods.Post && request.Path.StartsWithSegments("/v1/runs") && request.Path.Value?.EndsWith("/cancel", StringComparison.Ordinal) == true)
        {
            return "hub.cancel";
        }

        if (request.Path.StartsWithSegments("/v1/workers")
            || request.Path.Value?.EndsWith("/heartbeat", StringComparison.Ordinal) == true
            || request.Path.Value?.EndsWith("/complete", StringComparison.Ordinal) == true
            || request.Path.Value?.EndsWith("/fail", StringComparison.Ordinal) == true)
        {
            return "hub.admin";
        }

        return "hub.send";
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope)
        => principal.Claims
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);
}
