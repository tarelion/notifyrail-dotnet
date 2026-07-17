using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.CreateApiKey;

public sealed record CreateApiKeyResponse(
    [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("display_prefix")] string DisplayPrefix,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);
