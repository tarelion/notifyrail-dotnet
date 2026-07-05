using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessageDeliveries;

public sealed record GetMessageDeliveriesResponse(
    [property: JsonPropertyName("message_id")]
    Guid MessageId,

    [property: JsonPropertyName("deliveries")]
    IReadOnlyList<GetMessageDeliveryResponse> Deliveries);

public sealed record GetMessageDeliveryResponse(
    [property: JsonPropertyName("delivery_id")]
    Guid DeliveryId,

    [property: JsonPropertyName("recipient")]
    string Recipient,

    [property: JsonPropertyName("status")]
    string Status,

    [property: JsonPropertyName("attempt_count")]
    int AttemptCount,

    [property: JsonPropertyName("next_attempt_at")]
    DateTimeOffset? NextAttemptAt,

    [property: JsonPropertyName("provider_message_id")]
    string? ProviderMessageId,

    [property: JsonPropertyName("expires_at")]
    DateTimeOffset? ExpiresAt,

    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt,

    [property: JsonPropertyName("updated_at")]
    DateTimeOffset UpdatedAt,

    [property: JsonPropertyName("attempts")]
    IReadOnlyList<GetMessageDeliveryAttemptResponse> Attempts);

public sealed record GetMessageDeliveryAttemptResponse(
    [property: JsonPropertyName("attempt_number")]
    int AttemptNumber,

    [property: JsonPropertyName("provider")]
    string Provider,

    [property: JsonPropertyName("outcome")]
    string Outcome,

    [property: JsonPropertyName("provider_message_id")]
    string? ProviderMessageId,

    [property: JsonPropertyName("error_code")]
    string? ErrorCode,

    [property: JsonPropertyName("error_message")]
    string? ErrorMessage,

    [property: JsonPropertyName("attempted_at")]
    DateTimeOffset AttemptedAt);
