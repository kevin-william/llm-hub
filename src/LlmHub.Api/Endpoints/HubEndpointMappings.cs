using LlmHub.Application.Channels;
using LlmHub.Api.Authentication;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Runs;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Infrastructure.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Api.Endpoints;

public static class HubEndpointMappings
{
    public static void MapHubEndpoints(this WebApplication app)
    {
        var channels = app.MapGroup("/v1/channels");
        channels.MapPost("", CreateChannelAsync);
        channels.MapGet("/{channelId}", GetChannelAsync);
        channels.MapGet("/{channelId}/messages", GetHistoryAsync);
        channels.MapPost("/{channelId}/messages", SendMessageAsync);

        app.MapGet("/v1/messages/{messageId}", GetMessageAsync);
        app.MapGet("/v1/artifacts/{artifactId}", GetArtifactAsync);
        app.MapGet("/v1/runs/{runId}", GetRunAsync);
        app.MapPost("/v1/runs/{runId}/cancel", CancelRunAsync);
        app.MapPost("/v1/deliveries/{messageId}/ack", AckMessageAsync);
    }

    private static async Task<IResult> CreateChannelAsync(
        OpenChannelRequest request,
        HttpRequest httpRequest,
        IChannelService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(
                new CreateChannelCommand(RequestIdentity.Principal(httpRequest.HttpContext), request.Destination, request.Participants, request.MaxHops, request.RequiredCapabilities, RequestIdentity.Tenant(httpRequest.HttpContext)),
                cancellationToken);
            return Results.Created($"/v1/channels/{result.ChannelId}", new ChannelResponse(result.ChannelId, result.Destination, result.Participants, result.Ordering, result.MaxHops));
        }
        catch (DomainException exception)
        {
            return Problem(exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetChannelAsync(string channelId, HttpRequest request, IDbContextFactory<HubDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var channel = await context.Channels.AsNoTracking().SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var participants = await context.ChannelParticipants.AsNoTracking()
            .Where(item => item.ChannelId == channelId)
            .Select(item => item.PrincipalId)
            .ToArrayAsync(cancellationToken);
        if (channel.TenantId != RequestIdentity.Tenant(request.HttpContext) || !participants.Contains(RequestIdentity.Principal(request.HttpContext), StringComparer.Ordinal))
        {
            return Results.NotFound();
        }
        return Results.Ok(new ChannelResponse(channel.Id, channel.Destination, participants, channel.Ordering, channel.MaxHops));
    }

    private static async Task<IResult> GetHistoryAsync(
        string channelId,
        long? afterSequence,
        int? limit,
        HttpRequest request,
        IDbContextFactory<HubDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit ?? 50, 1, 100);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await CanAccessChannelAsync(context, channelId, RequestIdentity.Principal(request.HttpContext), RequestIdentity.Tenant(request.HttpContext), cancellationToken))
        {
            return Results.NotFound();
        }
        var records = await context.Messages.AsNoTracking()
            .Where(message => message.ChannelId == channelId && (!afterSequence.HasValue || message.Sequence > afterSequence.Value))
            .OrderBy(message => message.Sequence)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        var messages = records.Take(pageSize).Select(ToContract).ToArray();
        long? next = records.Length > pageSize ? messages[^1].Sequence : null;
        return Results.Ok(new MessageHistoryResponse(messages, next));
    }

    private static async Task<IResult> SendMessageAsync(
        string channelId,
        SendMessageRequest request,
        HttpRequest httpRequest,
        IMessageAcceptanceService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientMessageId))
        {
            return Problem("client_message_id is required.", StatusCodes.Status400BadRequest);
        }

        try
        {
            var principal = RequestIdentity.Principal(httpRequest.HttpContext);
            var result = await service.AcceptAsync(
                new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", request.ClientMessageId, request.Content, request.AwaitResponse, request.Attachments, RequestIdentity.Tenant(httpRequest.HttpContext)),
                cancellationToken);
            return Results.Accepted($"/v1/runs/{result.RunId}", result);
        }
        catch (DomainException exception) when (exception.Message == "SUBSCRIPTION_REQUIRED")
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, extensions: new Dictionary<string, object?> { ["code"] = exception.Message });
        }
        catch (DomainException exception)
        {
            return Problem(exception.Message, StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetMessageAsync(string messageId, HttpRequest request, IDbContextFactory<HubDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await context.Messages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        return message is null || !await CanAccessChannelAsync(context, message.ChannelId, RequestIdentity.Principal(request.HttpContext), RequestIdentity.Tenant(request.HttpContext), cancellationToken)
            ? Results.NotFound()
            : Results.Ok(ToContract(message));
    }

    private static async Task<IResult> GetArtifactAsync(
        string artifactId,
        HttpRequest request,
        IDbContextFactory<HubDbContext> contextFactory,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var artifact = await context.Artifacts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == artifactId, cancellationToken);
        if (artifact is null || !await CanAccessChannelAsync(context, artifact.ChannelId, RequestIdentity.Principal(request.HttpContext), RequestIdentity.Tenant(request.HttpContext), cancellationToken))
        {
            return Results.NotFound();
        }

        var store = services.GetService<IArtifactStore>();
        if (store is null)
        {
            return Results.Problem("Artifact storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var content = await store.GetAsync(artifact.StorageKey, cancellationToken);
        return Results.File(content, artifact.ContentType);
    }

    private static async Task<IResult> GetRunAsync(string runId, HttpRequest request, IDbContextFactory<HubDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        return run is null || !await CanAccessChannelAsync(context, run.ChannelId, RequestIdentity.Principal(request.HttpContext), RequestIdentity.Tenant(request.HttpContext), cancellationToken)
            ? Results.NotFound()
            : Results.Ok(new RunResponse(run.Id, run.ChannelId, run.InputMessageId, run.State.ToString().ToLowerInvariant(), run.AcceptedAt));
    }

    private static async Task<IResult> CancelRunAsync(string runId, HttpRequest request, IDbContextFactory<HubDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return Results.NotFound();
        }
        if (!await CanAccessChannelAsync(context, run.ChannelId, RequestIdentity.Principal(request.HttpContext), RequestIdentity.Tenant(request.HttpContext), cancellationToken))
        {
            return Results.NotFound();
        }

        if (run.State is RunState.Accepted or RunState.Queued or RunState.Leased or RunState.Running or RunState.RetryWait)
        {
            run.State = RunState.Cancelled;
            run.Version = Guid.NewGuid();
            await context.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new RunResponse(run.Id, run.ChannelId, run.InputMessageId, run.State.ToString().ToLowerInvariant(), run.AcceptedAt));
    }

    private static IResult AckMessageAsync(string messageId) => Results.NoContent();

    private static MessageResponse ToContract(MessageRecord message) => new(
        message.Id,
        message.ChannelId,
        message.Sequence,
        message.Sender,
        message.Recipient,
        message.Content,
        message.RootMessageId,
        message.ReplyToMessageId,
        message.CausationId,
        message.HopCount,
        message.CreatedAt);

    private static Task<bool> CanAccessChannelAsync(HubDbContext context, string channelId, string principal, string tenant, CancellationToken cancellationToken)
        => context.ChannelParticipants.AnyAsync(
            participant => participant.ChannelId == channelId && participant.PrincipalId == principal
                && context.Channels.Any(channel => channel.Id == channelId && channel.TenantId == tenant),
            cancellationToken);

    private static IResult Problem(string detail, int statusCode)
        => Results.Problem(detail, statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = detail });
}
