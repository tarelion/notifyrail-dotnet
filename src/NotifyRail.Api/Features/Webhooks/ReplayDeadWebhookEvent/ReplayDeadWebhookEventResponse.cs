using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.ReplayDeadWebhookEvent;

public sealed record ReplayDeadWebhookEventResponse(
    [property: JsonPropertyName("webhook_event_id")] Guid WebhookEventId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("message_id")] Guid MessageId,
    [property: JsonPropertyName("delivery_id")] Guid DeliveryId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("replayed_at")] DateTimeOffset ReplayedAt);
