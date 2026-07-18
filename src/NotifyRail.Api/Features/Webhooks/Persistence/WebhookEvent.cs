using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookEvent
{
    private WebhookEvent()
    {
    }

    public static WebhookEvent CreateDeliverySent(
        Guid apiClientId,
        Guid webhookEndpointId,
        Guid messageId,
        Guid deliveryId,
        string recipient,
        int sequence,
        DateTimeOffset occurredAt)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new WebhookEventEnvelope(
            id,
            "delivery.sent",
            1,
            occurredAt,
            new WebhookEventData(
                messageId,
                deliveryId,
                sequence,
                "sent",
                recipient)));

        return new WebhookEvent
        {
            Id = id,
            ApiClientId = apiClientId,
            WebhookEndpointId = webhookEndpointId,
            MessageId = messageId,
            DeliveryId = deliveryId,
            Type = "delivery.sent",
            Version = 1,
            Sequence = sequence,
            OccurredAt = occurredAt,
            Payload = payload,
            Status = "pending",
            AttemptCount = 0,
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
        };
    }

    public Guid Id { get; private set; }
    public Guid ApiClientId { get; private set; }
    public Guid WebhookEndpointId { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public string Type { get; private set; } = null!;
    public int Version { get; private set; }
    public int Sequence { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Payload { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public string? ClaimedBy { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private sealed record WebhookEventEnvelope(
        [property: JsonPropertyName("event_id")] Guid EventId,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("data")] WebhookEventData Data);

    private sealed record WebhookEventData(
        [property: JsonPropertyName("message_id")] Guid MessageId,
        [property: JsonPropertyName("delivery_id")] Guid DeliveryId,
        [property: JsonPropertyName("sequence")] int Sequence,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("recipient")] string Recipient);
}
