using LlmHub.Application.Channels;
using LlmHub.Application.Messaging;
using LlmHub.Application.Routing;
using LlmHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresChannelService : IChannelService
{
    private readonly IDbContextFactory<HubDbContext> contextFactory;
    private readonly HubQuotaOptions quotas;

    public PostgresChannelService(IDbContextFactory<HubDbContext> contextFactory, HubQuotaOptions? quotas = null)
    {
        this.contextFactory = contextFactory;
        this.quotas = quotas ?? HubQuotaOptions.Default;
    }

    public async Task<CreateChannelResult> CreateAsync(CreateChannelCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Destination))
        {
            throw new DomainException("A destination is required.");
        }

        var maxHops = command.MaxHops ?? 8;

        if (maxHops < 1)
        {
            throw new DomainException("The hop limit must be positive.");
        }

        var channelId = $"chn_{Guid.NewGuid():N}";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var channelCount = await context.ChannelParticipants.CountAsync(
            participant => participant.PrincipalId == command.OwnerPrincipalId
                && context.Channels.Any(channel => channel.Id == participant.ChannelId && channel.TenantId == command.TenantId),
            cancellationToken);
        if (channelCount >= quotas.MaxChannelsPerPrincipal)
        {
            context.AuditEvents.Add(new AuditRecord
            {
                Id = $"aud_{Guid.NewGuid():N}", EventName = "quota.channels.rejected", OccurredAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
            throw new DomainException("CHANNEL_QUOTA_EXCEEDED");
        }

        var destination = await ResolveDestinationAsync(context, command, cancellationToken);
        var participants = (command.Participants ?? [])
            .Append(command.OwnerPrincipalId)
            .Append(destination)
            .Where(participant => !string.IsNullOrWhiteSpace(participant))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (participants.Length < 2)
        {
            throw new DomainException("A channel requires at least two participants.");
        }

        context.Channels.Add(new ChannelRecord
        {
            Id = channelId,
            TenantId = command.TenantId,
            Destination = destination,
            Ordering = "serial",
            MaxHops = maxHops,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.ChannelParticipants.AddRange(participants.Select(participant => new ChannelParticipantRecord
        {
            ChannelId = channelId,
            PrincipalId = participant,
        }));
        await context.SaveChangesAsync(cancellationToken);

        return new CreateChannelResult(channelId, destination, participants, "serial", maxHops);
    }

    private static async Task<string> ResolveDestinationAsync(HubDbContext context, CreateChannelCommand command, CancellationToken cancellationToken)
    {
        var adapter = DeterministicAdapterRouter.Resolve(command.Destination);
        var requestedCapabilities = (command.RequiredCapabilities ?? [])
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var destinationSuffix = command.Destination["agent:".Length..];
        var isExplicitEndpoint = destinationSuffix.Contains('/', StringComparison.Ordinal);
        if (isExplicitEndpoint && requestedCapabilities.Length == 0)
        {
            return command.Destination;
        }

        var endpoints = await context.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Adapter == adapter && endpoint.Status == "online" && endpoint.TenantId == command.TenantId)
            .ToArrayAsync(cancellationToken);
        var endpoint = endpoints
            .Where(endpoint => !isExplicitEndpoint || endpoint.Address == command.Destination)
            .Where(endpoint => HasCapabilities(endpoint.CapabilitiesJson, requestedCapabilities))
            .OrderBy(endpoint => endpoint.Address, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return endpoint?.Address ?? throw new DomainException("ENDPOINT_NOT_AVAILABLE");
    }

    private static bool HasCapabilities(string serializedCapabilities, string[] requiredCapabilities)
    {
        if (requiredCapabilities.Length == 0)
        {
            return true;
        }

        var available = JsonSerializer.Deserialize<string[]>(serializedCapabilities) ?? [];
        return requiredCapabilities.All(available.Contains);
    }
}
