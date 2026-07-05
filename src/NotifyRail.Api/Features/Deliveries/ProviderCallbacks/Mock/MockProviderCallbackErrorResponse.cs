using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed record MockProviderCallbackErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
