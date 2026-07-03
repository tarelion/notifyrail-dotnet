using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed record CreateMessageErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
