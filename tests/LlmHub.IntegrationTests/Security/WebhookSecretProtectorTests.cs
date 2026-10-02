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
    public void NewPrimaryKeyDecryptsSecretsWrittenWithThePreviousKey()
    {
        const string previousKey = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";
        const string primaryKey = "AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=";
        var previous = new AesGcmWebhookSecretProtector(previousKey);
        var rotating = new AesGcmWebhookSecretProtector(primaryKey, [previousKey]);

        var previousSecret = previous.Protect("whsec_previous");
        var newSecret = rotating.Protect("whsec_current");

        Assert.Equal("whsec_previous", rotating.Unprotect(previousSecret));
        Assert.Equal("whsec_current", rotating.Unprotect(newSecret));
        Assert.StartsWith("v2:", newSecret, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPreviousKeyRejectsOlderSecrets()
    {
        const string previousKey = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";
        const string primaryKey = "AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=";
        var oldSecret = new AesGcmWebhookSecretProtector(previousKey).Protect("whsec_previous");

        Assert.Throws<WebhookSecretProtectionException>(() => new AesGcmWebhookSecretProtector(primaryKey).Unprotect(oldSecret));
    }

    [Fact]
    public void MissingKeyDoesNotAllowPlaintextFallback()
    {
        Assert.Throws<WebhookSecretProtectionException>(() => new UnavailableWebhookSecretProtector().Protect("whsec_example-secret"));
    }
}
