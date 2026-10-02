using System.Security.Cryptography;

namespace LlmHub.Infrastructure.Webhooks;

public interface IWebhookSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public sealed class WebhookSecretProtectionException : InvalidOperationException
{
    public WebhookSecretProtectionException(string message) : base(message)
    {
    }

    public WebhookSecretProtectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class AesGcmWebhookSecretProtector : IWebhookSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly KeyMaterial primaryKey;
    private readonly IReadOnlyDictionary<string, KeyMaterial> keys;

    public AesGcmWebhookSecretProtector(string base64Key) : this(base64Key, [])
    {
    }

    public AesGcmWebhookSecretProtector(string base64Key, IReadOnlyList<string> previousBase64Keys)
    {
        primaryKey = ParseKey(base64Key);
        keys = new[] { primaryKey }
            .Concat((previousBase64Keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Select(ParseKey))
            .GroupBy(key => key.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var cipher = new AesGcm(primaryKey.Value, TagSize);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag);
        return $"v2:{primaryKey.Id}:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    public string Unprotect(string protectedSecret)
    {
        var parts = protectedSecret.Split(':', StringSplitOptions.None);
        if (parts is ["v2", var keyId, var nonceValue, var tagValue, var ciphertextValue])
        {
            if (!keys.TryGetValue(keyId, out var key))
            {
                throw new WebhookSecretProtectionException("The stored webhook secret uses an unavailable encryption key.");
            }

            return Decrypt(key.Value, nonceValue, tagValue, ciphertextValue);
        }

        if (parts is ["v1", var legacyNonce, var legacyTag, var legacyCiphertext])
        {
            foreach (var key in keys.Values)
            {
                try
                {
                    return Decrypt(key.Value, legacyNonce, legacyTag, legacyCiphertext);
                }
                catch (WebhookSecretProtectionException)
                {
                    // Try the next configured previous key for secrets written before key identifiers existed.
                }
            }

            throw new WebhookSecretProtectionException("The stored webhook secret cannot be decrypted.");
        }

        throw new WebhookSecretProtectionException("The stored webhook secret has an unsupported format.");
    }

    private static string Decrypt(byte[] key, string nonceValue, string tagValue, string ciphertextValue)
    {
        try
        {
            var nonce = Convert.FromBase64String(nonceValue);
            var tag = Convert.FromBase64String(tagValue);
            var ciphertext = Convert.FromBase64String(ciphertextValue);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
            {
                throw new WebhookSecretProtectionException("The stored webhook secret is invalid.");
            }

            var plaintext = new byte[ciphertext.Length];
            using var cipher = new AesGcm(key, TagSize);
            cipher.Decrypt(nonce, ciphertext, tag, plaintext);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException exception)
        {
            throw new WebhookSecretProtectionException("The stored webhook secret is invalid.", exception);
        }
        catch (CryptographicException exception)
        {
            throw new WebhookSecretProtectionException("The stored webhook secret cannot be decrypted.", exception);
        }
    }

    private static KeyMaterial ParseKey(string base64Key)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new WebhookSecretProtectionException("Webhook:SecretEncryptionKey must be a base64-encoded 256-bit key.", exception);
        }

        if (key.Length != 32)
        {
            throw new WebhookSecretProtectionException("Webhook:SecretEncryptionKey must be a base64-encoded 256-bit key.");
        }

        return new KeyMaterial(Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant(), key);
    }

    private sealed record KeyMaterial(string Id, byte[] Value);
}

public sealed class UnavailableWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Message = "Webhook secret protection is not configured. Set Webhook:SecretEncryptionKey to a base64-encoded 256-bit key.";

    public string Protect(string secret) => throw new WebhookSecretProtectionException(Message);

    public string Unprotect(string protectedSecret) => throw new WebhookSecretProtectionException(Message);
}
