using LlmHub.Domain.Common;

namespace LlmHub.Domain.Endpoints;

public enum EndpointStatus
{
    Offline,
    Online,
    Draining,
}

public sealed class Endpoint
{
    private readonly HashSet<string> _capabilities;

    public Endpoint(
        EndpointId id,
        string address,
        string adapter,
        IEnumerable<string> capabilities,
        EndpointStatus status)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("An endpoint address is required.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(adapter))
        {
            throw new ArgumentException("An adapter is required.", nameof(adapter));
        }

        Id = id;
        Address = address;
        Adapter = adapter;
        Status = status;
        _capabilities = capabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .ToHashSet(StringComparer.Ordinal);
    }

    public EndpointId Id { get; }

    public string Address { get; }

    public string Adapter { get; }

    public EndpointStatus Status { get; private set; }

    public IReadOnlySet<string> Capabilities => _capabilities;

    public void SetStatus(EndpointStatus status) => Status = status;
}
