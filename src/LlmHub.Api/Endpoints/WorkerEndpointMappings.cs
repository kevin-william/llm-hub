using LlmHub.Application.Workers;
using LlmHub.Contracts.Workers;
using LlmHub.Domain.Common;

namespace LlmHub.Api.Endpoints;

public static class WorkerEndpointMappings
{
    public static void MapWorkerEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/workers/register", RegisterAsync);
        app.MapPost("/v1/workers/{workerId}/claims", ClaimAsync);
        app.MapPost("/v1/runs/{runId}/heartbeat", HeartbeatAsync);
        app.MapPost("/v1/runs/{runId}/complete", CompleteAsync);
        app.MapPost("/v1/runs/{runId}/fail", FailAsync);
    }

    private static async Task<IResult> RegisterAsync(RegisterWorkerRequest request, IWorkerGateway gateway, CancellationToken cancellationToken)
        => await ExecuteAsync(() => gateway.RegisterAsync(request, cancellationToken));

    private static async Task<IResult> ClaimAsync(string workerId, ClaimRequest request, IWorkerGateway gateway, CancellationToken cancellationToken)
    {
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

    private static Task<IResult> HeartbeatAsync(string runId, HeartbeatRequest request, IWorkerGateway gateway, CancellationToken cancellationToken)
        => ExecuteAsync(async () => Results.Ok(await gateway.HeartbeatAsync(runId, request, cancellationToken)));

    private static Task<IResult> CompleteAsync(string runId, CompleteRunRequest request, IWorkerGateway gateway, CancellationToken cancellationToken)
        => ExecuteAsync(() => gateway.CompleteAsync(runId, request, cancellationToken));

    private static Task<IResult> FailAsync(string runId, FailRunRequest request, IWorkerGateway gateway, CancellationToken cancellationToken)
        => ExecuteAsync(() => gateway.FailAsync(runId, request, cancellationToken));

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
}
