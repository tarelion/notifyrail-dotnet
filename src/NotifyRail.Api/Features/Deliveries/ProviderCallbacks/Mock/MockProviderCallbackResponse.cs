using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed record MockProviderCallbackResponse(
    [property: JsonPropertyName("delivery_id")]
    Guid DeliveryId,

    [property: JsonPropertyName("provider_message_id")]
    string ProviderMessageId,

    [property: JsonPropertyName("status")]
    string Status,

    [property: JsonPropertyName("updated_at")]
    DateTimeOffset UpdatedAt);
