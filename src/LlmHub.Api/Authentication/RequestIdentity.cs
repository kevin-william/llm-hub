using System.Security.Claims;

namespace LlmHub.Api.Authentication;

public static class RequestIdentity
{
    public static string Principal(HttpContext context)
        => context.User.FindFirst("sub")?.Value is { Length: > 0 } subject
            ? $"principal:oidc/{subject}"
            : context.Request.Headers["X-Principal-Id"].ToString() is { Length: > 0 } principal
            ? principal
            : "principal:development";

    public static string Tenant(HttpContext context)
        => context.User.Claims.FirstOrDefault(claim => claim.Type is "tenant_id" or "tid" or "tenant")?.Value is { Length: > 0 } tenant
            ? $"tenant:oidc/{tenant}"
            : "tenant:development";

    public static bool HasTenantClaim(ClaimsPrincipal principal)
        => principal.Claims.Any(claim => claim.Type is "tenant_id" or "tid" or "tenant" && !string.IsNullOrWhiteSpace(claim.Value));
}
