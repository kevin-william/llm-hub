using LlmHub.Application.Workers;
using LlmHub.Api.Authentication;
using LlmHub.Contracts.Artifacts;
using LlmHub.Contracts.Workers;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Artifacts;

namespace LlmHub.Api.Endpoints;

public static class WorkerEndpointMappings
{
    public static void MapWorkerEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/workers/register", RegisterAsync);
        app.MapPost("/v1/workers/{workerId}/claims", ClaimAsync);
        app.MapPost("/v1/workers/{workerId}/artifacts", UploadArtifactAsync);
        app.MapPost("/v1/runs/{runId}/heartbeat", HeartbeatAsync);
        app.MapPost("/v1/runs/{runId}/complete", CompleteAsync);
        app.MapPost("/v1/runs/{runId}/fail", FailAsync);
    }

    private static async Task<IResult> RegisterAsync(RegisterWorkerRequest request, HttpContext httpContext, WorkerIdentityValidator identity, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
        if (!WorkerIdentityValidator.CanAccessWorker(httpContext.User, request.WorkerId))
        {
            return Results.Forbid();
        }

        return await ExecuteAsync(() => gateway.RegisterAsync(request, cancellationToken, RequestIdentity.Tenant(httpContext)));
    }

    private static async Task<IResult> ClaimAsync(string workerId, ClaimRequest request, HttpContext httpContext, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
        if (!WorkerIdentityValidator.CanAccessWorker(httpContext.User, workerId))
        {
            return Results.Forbid();
        }

        try
        {
            var claim = await gateway.ClaimAsync(workerId, cancellationToken);
            return claim is null ? Results.NoContent() : Results.Ok(claim);
        }
        catch (DomainException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> UploadArtifactAsync(
        string workerId,
        HttpRequest request,
        HttpContext httpContext,
        WorkerIdentityValidator identity,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!WorkerIdentityValidator.CanAccessWorker(httpContext.User, workerId))
        {
            return Results.Forbid();
        }

        var store = services.GetService<IArtifactStore>();
        if (store is null)
        {
            return Results.Problem("Artifact storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (request.ContentLength is > 20 * 1024 * 1024)
        {
            return Results.Problem("Artifact content must be between 1 byte and 20 MiB.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var content = await ReadArtifactAsync(request, cancellationToken);
            var contentType = request.ContentType?.Split(';', 2)[0] ?? "application/octet-stream";
            var expectedHash = request.Headers["X-Artifact-Sha256"].ToString();
            var stored = await store.PutAsync(content, contentType, string.IsNullOrWhiteSpace(expectedHash) ? null : expectedHash, cancellationToken);
            return Results.Created(
                $"/v1/artifacts/{stored.ContentHash}",
                new ArtifactUploadResponse(stored.StorageKey, stored.ContentHash, stored.ContentType, stored.Length));
        }
        catch (ArtifactValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HeartbeatAsync(string runId, HeartbeatRequest request, HttpContext httpContext, WorkerIdentityValidator identity, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
        if (!await identity.CanAccessRunAsync(httpContext.User, runId, cancellationToken))
        {
            return Results.Forbid();
        }

        return await ExecuteAsync(async () => Results.Ok(await gateway.HeartbeatAsync(runId, request, cancellationToken)));
    }

    private static async Task<IResult> CompleteAsync(string runId, CompleteRunRequest request, HttpContext httpContext, WorkerIdentityValidator identity, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
        if (!await identity.CanAccessRunAsync(httpContext.User, runId, cancellationToken))
        {
            return Results.Forbid();
        }

        return await ExecuteAsync(() => gateway.CompleteAsync(runId, request, cancellationToken));
    }

    private static async Task<IResult> FailAsync(string runId, FailRunRequest request, HttpContext httpContext, WorkerIdentityValidator identity, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
        if (!await identity.CanAccessRunAsync(httpContext.User, runId, cancellationToken))
        {
            return Results.Forbid();
        }

        return await ExecuteAsync(() => gateway.FailAsync(runId, request, cancellationToken));
    }

    private static async Task<IResult> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Results.NoContent();
        }
        catch (DomainException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<byte[]> ReadArtifactAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        const int maximumBytes = 20 * 1024 * 1024;
        await using var destination = new MemoryStream();
        var buffer = new byte[80 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (destination.Length + read > maximumBytes)
            {
                throw new ArtifactValidationException("Artifact content must be between 1 byte and 20 MiB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }
}
