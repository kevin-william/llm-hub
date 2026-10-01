using System.Net.Http.Json;

namespace LlmHub.Infrastructure.Webhooks;

public interface IWebhookVerificationClient
{
    Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken);
}

public sealed class HttpWebhookVerificationClient(HttpClient httpClient) : IWebhookVerificationClient
{
    public async Task<bool> VerifyAsync(Uri callback, string challenge, CancellationToken cancellationToken)
    {
        await CallbackPolicy.ValidateResolvedDestinationAsync(callback, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, callback)
        {
            Content = JsonContent.Create(new { type = "hub.webhook.challenge", challenge }),
        };
        request.Headers.Add("webhook-challenge", challenge);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            && response.Headers.TryGetValues("webhook-challenge", out var values)
            && values.Contains(challenge, StringComparer.Ordinal);
    }
}
