using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public sealed record CreateApiClientErrorResponse(
    [property: JsonPropertyName("error")] string Error);
