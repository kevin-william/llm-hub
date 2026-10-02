namespace LlmHub.Api.Authentication;

public sealed class TenantAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/v1")
            && context.User.Identity?.IsAuthenticated == true
            && !RequestIdentity.HasTenantClaim(context.User))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}
