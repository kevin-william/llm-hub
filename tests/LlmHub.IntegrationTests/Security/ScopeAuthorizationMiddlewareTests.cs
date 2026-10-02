using System.Security.Claims;
using LlmHub.Api.Authentication;
using Microsoft.AspNetCore.Http;

namespace LlmHub.IntegrationTests.Security;

public sealed class ScopeAuthorizationMiddlewareTests
{
    [Fact]
    public async Task ReadScopeAllowsReadButNotSend()
    {
        var middleware = new ScopeAuthorizationMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var read = Context("GET", "/v1/channels/chn_1", "hub.read");
        var send = Context("POST", "/v1/channels/chn_1/messages", "hub.read");

        await middleware.InvokeAsync(read);
        await middleware.InvokeAsync(send);

        Assert.Equal(StatusCodes.Status204NoContent, read.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, send.Response.StatusCode);
    }

    [Fact]
    public async Task WorkerOperationsRequireAdminScope()
    {
        var middleware = new ScopeAuthorizationMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var workerClaim = Context("POST", "/v1/workers/worker_1/claims", "hub.send");
        var completion = Context("POST", "/v1/runs/run_1/complete", "hub.send");
        var administrator = Context("POST", "/v1/runs/run_1/complete", "hub.admin");

        await middleware.InvokeAsync(workerClaim);
        await middleware.InvokeAsync(completion);
        await middleware.InvokeAsync(administrator);

        Assert.Equal(StatusCodes.Status403Forbidden, workerClaim.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, completion.Response.StatusCode);
        Assert.Equal(StatusCodes.Status204NoContent, administrator.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string method, string path, string scope)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", scope)], "test"));
        return context;
    }
}
