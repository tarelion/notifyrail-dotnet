using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.InspectWebhookEndpoint;

public sealed record InspectWebhookEndpointResponse(
    [property: JsonPropertyName("webhook_endpoint_id")] Guid WebhookEndpointId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("disabled_at")] DateTimeOffset? DisabledAt);
