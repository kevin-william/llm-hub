using System.Security.Claims;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Api.Authentication;

public sealed class WorkerIdentityValidator(IDbContextFactory<HubDbContext> contextFactory)
{
    public static bool CanAccessWorker(ClaimsPrincipal principal, string workerId)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        return principal.FindFirst("worker_id")?.Value == workerId;
    }

    public async Task<bool> CanAccessRunAsync(ClaimsPrincipal principal, string runId, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var workerId = principal.FindFirst("worker_id")?.Value;
        var tenantId = principal.Claims.FirstOrDefault(claim => claim.Type is "tenant_id" or "tid" or "tenant")?.Value;
        if (string.IsNullOrWhiteSpace(workerId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (from run in context.Runs.AsNoTracking()
                      join channel in context.Channels.AsNoTracking() on run.ChannelId equals channel.Id
                      where run.Id == runId && run.WorkerId == workerId && channel.TenantId == $"tenant:oidc/{tenantId}"
                      select run.Id).AnyAsync(
            cancellationToken);
    }
}
