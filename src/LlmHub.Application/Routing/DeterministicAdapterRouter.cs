using LlmHub.Domain.Common;

namespace LlmHub.Application.Routing;

public static class DeterministicAdapterRouter
{
    public static string Resolve(string destination)
    {
        const string prefix = "agent:";
        if (!destination.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new DomainException("The destination does not identify an agent adapter.");
        }

        var adapter = destination[prefix.Length..].Split('/', 2)[0];
        if (adapter.Length is < 1 or > 64
            || !adapter.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new DomainException("The destination contains an invalid adapter identifier.");
        }

        return adapter;
    }
}
