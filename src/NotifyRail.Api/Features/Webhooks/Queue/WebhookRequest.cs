namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookRequest(
    Guid EventId,
    string EndpointUrl,
    string Body,
    byte[] ProtectedSecret);
