using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessageReport;

public sealed record GetMessageReportErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
