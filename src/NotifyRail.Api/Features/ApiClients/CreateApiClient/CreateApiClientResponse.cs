using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public sealed record CreateApiClientResponse(
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("display_prefix")] string DisplayPrefix,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
