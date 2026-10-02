using System.ComponentModel;
using System.Text.Json;
using LlmHub.Application.Channels;
using LlmHub.Api.Authentication;
using LlmHub.Application.Events;
using LlmHub.Application.Messaging;
using LlmHub.Contracts.Channels;
using LlmHub.Contracts.Events;
using LlmHub.Contracts.Messages;
using LlmHub.Contracts.Runs;
using LlmHub.Domain.Runs;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace LlmHub.Api.Mcp;

[McpServerToolType]
public sealed class HubMcpTools(
    IChannelService channels,
    IMessageAcceptanceService messages,
    ISubscriptionService subscriptions,
    IDbContextFactory<HubDbContext> contextFactory,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool, Description("Lists the Hub endpoints known to the control plane.")]
    public async Task<object[]> ListEndpoints(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var endpoints = await context.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.TenantId == Tenant())
            .OrderBy(endpoint => endpoint.Id)
            .ToArrayAsync(cancellationToken);
        return endpoints
            .Select(endpoint => (object)new
            {
                endpoint.Id,
                endpoint.Address,
                endpoint.Adapter,
                endpoint.Status,
                Capabilities = JsonSerializer.Deserialize<string[]>(endpoint.CapabilitiesJson) ?? [],
            })
            .ToArray();
    }

    [McpServerTool, Description("Opens a channel for the caller and a destination endpoint.")]
    public async Task<ChannelResponse> OpenChannel(string destination, string[]? participants = null, int? maxHops = null, string[]? requiredCapabilities = null, CancellationToken cancellationToken = default)
    {
        var result = await channels.CreateAsync(new CreateChannelCommand(Principal(), destination, participants, maxHops, requiredCapabilities, Tenant()), cancellationToken);
        return new ChannelResponse(result.ChannelId, result.Destination, result.Participants, result.Ordering, result.MaxHops);
    }

    [McpServerTool, Description("Sends an immutable message to a channel and queues its run.")]
    public Task<AcceptMessageResult> SendMessage(string channelId, string clientMessageId, string content, bool awaitResponse, CancellationToken cancellationToken) =>
        messages.AcceptAsync(new AcceptMessageCommand(channelId, Principal(), Principal(), "agent:opencode/default", clientMessageId, content, awaitResponse, TenantId: Tenant()), cancellationToken);

    [McpServerTool, Description("Gets one immutable message by identifier.")]
    public async Task<MessageResponse?> GetMessage(string messageId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await context.Messages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        return message is null || !await CanAccessChannelAsync(context, message.ChannelId, cancellationToken) ? null : ToResponse(message);
    }

    [McpServerTool, Description("Gets ordered channel history after an optional sequence cursor.")]
    public async Task<MessageHistoryResponse?> GetChannelHistory(string channelId, long? afterSequence, int? limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await CanAccessChannelAsync(context, channelId, cancellationToken))
        {
            return new MessageHistoryResponse([], null);
        }

        var pageSize = Math.Clamp(limit ?? 50, 1, 100);
        var records = await context.Messages.AsNoTracking().Where(item => item.ChannelId == channelId && (!afterSequence.HasValue || item.Sequence > afterSequence.Value))
            .OrderBy(item => item.Sequence).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var response = records.Take(pageSize).Select(ToResponse).ToArray();
        return new MessageHistoryResponse(response, records.Length > pageSize ? response[^1].Sequence : null);
    }

    [McpServerTool, Description("Gets the state of a queued or completed run.")]
    public async Task<RunResponse?> GetRun(string runId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        return run is null || !await CanAccessChannelAsync(context, run.ChannelId, cancellationToken)
            ? null
            : new RunResponse(run.Id, run.ChannelId, run.InputMessageId, run.State.ToString().ToLowerInvariant(), run.AcceptedAt);
    }

    [McpServerTool, Description("Requests cancellation for a run that has not completed.")]
    public async Task<RunResponse?> CancelRun(string runId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.Runs.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (!await CanAccessChannelAsync(context, run.ChannelId, cancellationToken))
        {
            return null;
        }

        if (run.State is RunState.Accepted or RunState.Queued or RunState.Leased or RunState.Running or RunState.RetryWait)
        {
            run.State = RunState.Cancelled;
            run.Version = Guid.NewGuid();
            await context.SaveChangesAsync(cancellationToken);
        }

        return new RunResponse(run.Id, run.ChannelId, run.InputMessageId, run.State.ToString().ToLowerInvariant(), run.AcceptedAt);
    }

    [McpServerTool, Description("Acknowledges delivery of a message. The operation is idempotent.")]
    public Task AckMessage(string messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    [McpServerTool, Description("Creates a verified persistent callback subscription for message-created events in a channel.")]
    public Task<SubscriptionResponse> SubscribeEvents(
        string channelId,
        string callbackUrl,
        string secret,
        int durationSeconds,
        CancellationToken cancellationToken) =>
        subscriptions.CreateAsync(
            Principal(),
            Tenant(),
            new CreateSubscriptionRequest(channelId, "message.created", callbackUrl, secret, durationSeconds),
            cancellationToken);

    [McpServerTool, Description("Removes a persistent event callback subscription owned by the caller.")]
    public Task UnsubscribeEvents(string subscriptionId, CancellationToken cancellationToken) =>
        subscriptions.RemoveAsync(Principal(), Tenant(), subscriptionId, cancellationToken);

    private Task<bool> CanAccessChannelAsync(HubDbContext context, string channelId, CancellationToken cancellationToken) =>
        context.ChannelParticipants.AnyAsync(
            participant => participant.ChannelId == channelId && participant.PrincipalId == Principal()
                && context.Channels.Any(channel => channel.Id == channelId && channel.TenantId == Tenant()),
            cancellationToken);

    private string Principal() => RequestIdentity.Principal(httpContextAccessor.HttpContext ?? new DefaultHttpContext());

    private string Tenant() => RequestIdentity.Tenant(httpContextAccessor.HttpContext ?? new DefaultHttpContext());

    private static MessageResponse ToResponse(MessageRecord message) => new(
        message.Id, message.ChannelId, message.Sequence, message.Sender, message.Recipient, message.Content,
        message.RootMessageId, message.ReplyToMessageId, message.CausationId, message.HopCount, message.CreatedAt);
}
