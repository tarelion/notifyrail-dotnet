namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookRequest(
    Guid EventId,
    Guid ApiClientId,
    Guid MessageId,
    Guid DeliveryId,
    string EndpointUrl,
    string Body,
    byte[] ProtectedSecret,
    string? SourceTraceParent);
