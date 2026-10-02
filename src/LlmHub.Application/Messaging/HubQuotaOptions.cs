namespace LlmHub.Application.Messaging;

public sealed record HubQuotaOptions(
    int MaxMessageBytes = 65_536,
    int MaxAttachments = 16,
    int MaxActiveRunsPerPrincipal = 10,
    int MaxChannelsPerPrincipal = 100,
    int MaxActiveSubscriptionsPerPrincipal = 20)
{
    public static readonly HubQuotaOptions Default = new();
}
