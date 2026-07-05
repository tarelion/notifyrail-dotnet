using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessageDeliveries;

public sealed record GetMessageDeliveriesErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
