using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public sealed record CreateApiClientRequest(
    [property: JsonPropertyName("name")] string Name);
