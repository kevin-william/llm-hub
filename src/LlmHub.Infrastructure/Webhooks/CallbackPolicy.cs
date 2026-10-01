using System.Net;
using System.Net.Sockets;

namespace LlmHub.Infrastructure.Webhooks;

public sealed class UnsafeWebhookDestinationException(string message) : InvalidOperationException(message);

public static class CallbackPolicy
{
    public static void ValidateUri(Uri callback)
    {
        if (!callback.IsAbsoluteUri || callback.Scheme != Uri.UriSchemeHttps || callback.UserInfo.Length > 0 || callback.Port is not 443 and not -1)
        {
            throw new UnsafeWebhookDestinationException("Webhook callbacks must be HTTPS without user info and use port 443.");
        }

        if (string.Equals(callback.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(callback.Host, out var address) && IsUnsafe(address)))
        {
            throw new UnsafeWebhookDestinationException("The callback destination is not publicly routable.");
        }
    }

    public static async Task ValidateResolvedDestinationAsync(Uri callback, CancellationToken cancellationToken)
    {
        ValidateUri(callback);
        var addresses = await Dns.GetHostAddressesAsync(callback.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsUnsafe))
        {
            throw new UnsafeWebhookDestinationException("The callback resolves to a non-public address.");
        }
    }

    private static bool IsUnsafe(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast;
    }
}
