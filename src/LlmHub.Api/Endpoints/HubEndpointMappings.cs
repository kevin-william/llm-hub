using LlmHub.Application.Channels;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Runs;
using LlmHub.Domain.Common;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
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
                new CreateChannelCommand(Principal(httpRequest), request.Destination, request.Participants, request.MaxHops),
                cancellationToken);
            return Results.Created($"/v1/channels/{result.ChannelId}", new ChannelResponse(result.ChannelId, result.Participants, result.Ordering, result.MaxHops));
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
        if (!participants.Contains(Principal(request), StringComparer.Ordinal))
        {
            return Results.NotFound();
        }
        return Results.Ok(new ChannelResponse(channel.Id, participants, channel.Ordering, channel.MaxHops));
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
        if (!await CanAccessChannelAsync(context, channelId, Principal(request), cancellationToken))
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
            var principal = Principal(httpRequest);
            var result = await service.AcceptAsync(
                new AcceptMessageCommand(channelId, principal, principal, "agent:opencode/default", request.ClientMessageId, request.Content, request.AwaitResponse, request.Attachments),
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
        return message is null || !await CanAccessChannelAsync(context, message.ChannelId, Principal(request), cancellationToken)
            ? Results.NotFound()
            : Results.Ok(ToContract(message));
    }

    private static async Task<IResult> GetRunAsync(string runId, HttpRequest request, IDbContextFactory<HubDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        return run is null || !await CanAccessChannelAsync(context, run.ChannelId, Principal(request), cancellationToken)
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
        if (!await CanAccessChannelAsync(context, run.ChannelId, Principal(request), cancellationToken))
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

    private static string Principal(HttpRequest request)
        => request.HttpContext.User.FindFirst("sub")?.Value is { Length: > 0 } subject
            ? $"principal:oidc/{subject}"
            : request.Headers.TryGetValue("X-Principal-Id", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : "principal:development";

    private static Task<bool> CanAccessChannelAsync(HubDbContext context, string channelId, string principal, CancellationToken cancellationToken)
        => context.ChannelParticipants.AnyAsync(
            participant => participant.ChannelId == channelId && participant.PrincipalId == principal,
            cancellationToken);

    private static IResult Problem(string detail, int statusCode)
        => Results.Problem(detail, statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = detail });
}
