using LlmHub.Application.Channels;
using LlmHub.Application.Messaging;
using LlmHub.Domain.Common;
using LlmHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmHub.IntegrationTests.Adapters;

public sealed class CapabilityRoutingTests
{
    [Fact]
    public async Task GenericAdapterDestinationSelectsTheFirstCompatibleOnlineEndpoint()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Endpoints.AddRange(
                new EndpointRecord { Id = "endpoint_z", Address = "agent:echo/zulu", Adapter = "echo", CapabilitiesJson = "[\"role:reviewer\"]", Status = "online" },
                new EndpointRecord { Id = "endpoint_a", Address = "agent:echo/alpha", Adapter = "echo", CapabilitiesJson = "[\"role:reviewer\",\"files\"]", Status = "online" },
                new EndpointRecord { Id = "endpoint_offline", Address = "agent:echo/offline", Adapter = "echo", CapabilitiesJson = "[\"role:reviewer\",\"files\"]", Status = "offline" });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var channels = new PostgresChannelService(factory);
        var channel = await channels.CreateAsync(
            new CreateChannelCommand("principal:router", "agent:echo", null, null, ["role:reviewer", "files"]),
            CancellationToken.None);
        var accepted = await new PostgresMessageAcceptanceService(factory).AcceptAsync(
            new AcceptMessageCommand(channel.ChannelId, "principal:router", "principal:router", "agent:opencode/default", "capability-route", "review this", false),
            CancellationToken.None);
        await using var verify = await factory.CreateDbContextAsync(CancellationToken.None);

        Assert.Equal("agent:echo/alpha", channel.Destination);
        Assert.Equal("agent:echo/alpha", (await verify.Channels.SingleAsync(item => item.Id == channel.ChannelId)).Destination);
        Assert.Equal("echo", (await verify.Runs.SingleAsync(item => item.Id == accepted.RunId)).Adapter);
    }

    [Fact]
    public async Task CapabilityRouteRejectsWhenNoCompatibleEndpointIsOnline()
    {
        var factory = CreateFactory();
        await using (var seed = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            seed.Endpoints.Add(new EndpointRecord
            {
                Id = "endpoint_researcher", Address = "agent:echo/researcher", Adapter = "echo",
                CapabilitiesJson = "[\"role:researcher\"]", Status = "online",
            });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var exception = await Assert.ThrowsAsync<DomainException>(() => new PostgresChannelService(factory).CreateAsync(
            new CreateChannelCommand("principal:router", "agent:echo", null, null, ["role:reviewer"]),
            CancellationToken.None));

        Assert.Equal("ENDPOINT_NOT_AVAILABLE", exception.Message);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>().UseInMemoryDatabase($"capability-routing-{Guid.NewGuid():N}").Options;
        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);

        public Task<HubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
