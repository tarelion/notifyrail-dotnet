using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.ListApiKeys;

public sealed record ApiKeyMetadataResponse(
    [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
    [property: JsonPropertyName("display_prefix")] string DisplayPrefix,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("last_used_at")] DateTimeOffset? LastUsedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("revoked_at")] DateTimeOffset? RevokedAt);
