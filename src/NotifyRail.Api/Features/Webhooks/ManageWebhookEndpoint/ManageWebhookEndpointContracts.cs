using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.ManageWebhookEndpoint;

public sealed record RegisterWebhookEndpointRequest(
    [property: JsonPropertyName("url")] string? Url);

public sealed record RegisterWebhookEndpointResponse(
    [property: JsonPropertyName("webhook_endpoint_id")] Guid WebhookEndpointId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("webhook_secret")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? WebhookSecret);

public sealed record WebhookEndpointResponse(
    [property: JsonPropertyName("webhook_endpoint_id")] Guid WebhookEndpointId,
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("disabled_at")] DateTimeOffset? DisabledAt);

public sealed record WebhookEndpointErrorResponse(
    [property: JsonPropertyName("error")] string Error);
