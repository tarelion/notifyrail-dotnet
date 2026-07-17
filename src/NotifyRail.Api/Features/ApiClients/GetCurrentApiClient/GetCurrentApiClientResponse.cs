using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.GetCurrentApiClient;

public sealed record GetCurrentApiClientResponse(
    [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
    [property: JsonPropertyName("name")] string Name);
