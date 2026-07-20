namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookRequest(
    Guid EventId,
    Guid ApiClientId,
    string EndpointUrl,
    string Body,
    byte[] ProtectedSecret);
