using LlmHub.Infrastructure.Webhooks;

namespace LlmHub.IntegrationTests.Security;

public sealed class WebhookSecretProtectorTests
{
    private const string Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void ProtectRoundTripsWithoutKeepingThePlaintext()
    {
        var protector = new AesGcmWebhookSecretProtector(Key);

        var protectedValue = protector.Protect("whsec_example-secret");

        Assert.DoesNotContain("whsec_example-secret", protectedValue, StringComparison.Ordinal);
        Assert.Equal("whsec_example-secret", protector.Unprotect(protectedValue));
    }

    [Fact]
    public void TamperedProtectedSecretIsRejected()
    {
        var protector = new AesGcmWebhookSecretProtector(Key);
        var protectedValue = protector.Protect("whsec_example-secret");
        var tampered = string.Concat(protectedValue[..^1], protectedValue[^1] == 'A' ? "B" : "A");

        Assert.Throws<WebhookSecretProtectionException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void MissingKeyDoesNotAllowPlaintextFallback()
    {
        Assert.Throws<WebhookSecretProtectionException>(() => new UnavailableWebhookSecretProtector().Protect("whsec_example-secret"));
    }
}
