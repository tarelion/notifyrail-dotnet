using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.InspectDeadWebhookEvent;

public sealed record InspectDeadWebhookEventResponse(
    [property: JsonPropertyName("webhook_event_id")] Guid WebhookEventId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("message_id")] Guid MessageId,
    [property: JsonPropertyName("delivery_id")] Guid DeliveryId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("automatic_attempt_deadline_at")]
    DateTimeOffset? AutomaticAttemptDeadlineAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("attempts")]
    IReadOnlyList<DeadWebhookAttemptResponse> Attempts);

public sealed record DeadWebhookAttemptResponse(
    [property: JsonPropertyName("attempt_number")] int AttemptNumber,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("http_status_code")] int? HttpStatusCode,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("attempted_at")] DateTimeOffset AttemptedAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset CompletedAt,
    [property: JsonPropertyName("latency_milliseconds")] long LatencyMilliseconds);

public sealed record ListDeadWebhookEventsResponse(
    [property: JsonPropertyName("webhook_events")]
    IReadOnlyList<DeadWebhookEventSummaryResponse> WebhookEvents);

public sealed record DeadWebhookEventSummaryResponse(
    [property: JsonPropertyName("webhook_event_id")] Guid WebhookEventId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("message_id")] Guid MessageId,
    [property: JsonPropertyName("delivery_id")] Guid DeliveryId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("automatic_attempt_deadline_at")]
    DateTimeOffset? AutomaticAttemptDeadlineAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);
