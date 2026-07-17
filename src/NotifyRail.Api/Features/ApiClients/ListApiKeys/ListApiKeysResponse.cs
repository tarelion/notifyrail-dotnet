using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.ListApiKeys;

public sealed record ListApiKeysResponse(
    [property: JsonPropertyName("api_keys")] IReadOnlyList<ApiKeyMetadataResponse> ApiKeys);
