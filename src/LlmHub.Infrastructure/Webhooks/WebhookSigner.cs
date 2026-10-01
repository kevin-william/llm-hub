using System.Security.Cryptography;
using System.Text;

namespace LlmHub.Infrastructure.Webhooks;

public sealed record WebhookSignature(string EventId, string Timestamp, string Value);

public static class WebhookSigner
{
    public static WebhookSignature Create(string secret, string eventId, string timestamp, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var key = DecodeSecret(secret);
        var signed = Encoding.UTF8.GetBytes($"{eventId}.{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(key, signed);
        return new WebhookSignature(eventId, timestamp, $"v1,{Convert.ToBase64String(hash)}");
    }

    public static bool Verify(string secret, string eventId, string timestamp, string payload, string signature)
    {
        var expected = Encoding.UTF8.GetBytes(Create(secret, eventId, timestamp, payload).Value);
        var received = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private static byte[] DecodeSecret(string secret)
    {
        var encoded = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret;
        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(secret);
        }
    }
}
