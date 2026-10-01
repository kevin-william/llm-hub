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
    private readonly byte[] key;

    public AesGcmWebhookSecretProtector(string base64Key)
    {
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
    }

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var cipher = new AesGcm(key, TagSize);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag);
        return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    public string Unprotect(string protectedSecret)
    {
        var parts = protectedSecret.Split(':', StringSplitOptions.None);
        if (parts.Length != 4 || parts[0] != "v1")
        {
            throw new WebhookSecretProtectionException("The stored webhook secret has an unsupported format.");
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var ciphertext = Convert.FromBase64String(parts[3]);
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
}

public sealed class UnavailableWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Message = "Webhook secret protection is not configured. Set Webhook:SecretEncryptionKey to a base64-encoded 256-bit key.";

    public string Protect(string secret) => throw new WebhookSecretProtectionException(Message);

    public string Unprotect(string protectedSecret) => throw new WebhookSecretProtectionException(Message);
}
