namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed record ProviderRequest(
    string IdempotencyKey,
    string Recipient,
    string Channel,
    string SenderTitle,
    string Body);
