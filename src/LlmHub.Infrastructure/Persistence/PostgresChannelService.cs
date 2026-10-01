using LlmHub.Application.Channels;
using LlmHub.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.Infrastructure.Persistence;

public sealed class PostgresChannelService(IDbContextFactory<HubDbContext> contextFactory) : IChannelService
{
    public async Task<CreateChannelResult> CreateAsync(CreateChannelCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Destination))
        {
            throw new DomainException("A destination is required.");
        }

        var participants = (command.Participants ?? [])
            .Append(command.OwnerPrincipalId)
            .Append(command.Destination)
            .Where(participant => !string.IsNullOrWhiteSpace(participant))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var maxHops = command.MaxHops ?? 8;

        if (participants.Length < 2)
        {
            throw new DomainException("A channel requires at least two participants.");
        }

        if (maxHops < 1)
        {
            throw new DomainException("The hop limit must be positive.");
        }

        var channelId = $"chn_{Guid.NewGuid():N}";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Channels.Add(new ChannelRecord
        {
            Id = channelId,
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

        return new CreateChannelResult(channelId, participants, "serial", maxHops);
    }
}
