using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessage;

public sealed record GetMessageErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
