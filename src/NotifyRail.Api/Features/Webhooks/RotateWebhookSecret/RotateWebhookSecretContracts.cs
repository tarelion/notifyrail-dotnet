using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Webhooks.RotateWebhookSecret;

public sealed record RotateWebhookSecretResponse(
    [property: JsonPropertyName("webhook_secret")] string WebhookSecret,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("overlap_expires_at")]
    DateTimeOffset OverlapExpiresAt);
