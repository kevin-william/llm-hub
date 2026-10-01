using LlmHub.Domain.Common;

namespace LlmHub.Application.Routing;

public static class DeterministicAdapterRouter
{
    private static readonly HashSet<string> SupportedAdapters = new(StringComparer.Ordinal) { "echo", "opencode" };

    public static string Resolve(string destination)
    {
        const string prefix = "agent:";
        if (!destination.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new DomainException("The destination does not identify an agent adapter.");
        }

        var adapter = destination[prefix.Length..].Split('/', 2)[0];
        if (!SupportedAdapters.Contains(adapter))
        {
            throw new DomainException("ADAPTER_NOT_AVAILABLE");
        }

        return adapter;
    }
}
