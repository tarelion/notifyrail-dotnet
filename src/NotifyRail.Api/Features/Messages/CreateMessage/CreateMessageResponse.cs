using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed record CreateMessageResponse(
    [property: JsonPropertyName("message_id")]
    Guid MessageId,

    [property: JsonPropertyName("delivery_count")]
    long DeliveryCount,

    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt);
