using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed record MockProviderCallbackRequest(
    [property: JsonPropertyName("provider_message_id")]
    string? ProviderMessageId,

    [property: JsonPropertyName("status")]
    string? Status);
