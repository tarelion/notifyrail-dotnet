using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.CreateApiKey;

public sealed record CreateApiKeyRequest(
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);
