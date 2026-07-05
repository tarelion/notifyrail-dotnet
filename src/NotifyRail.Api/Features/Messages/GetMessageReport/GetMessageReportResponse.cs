using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessageReport;

public sealed record GetMessageReportResponse(
    [property: JsonPropertyName("message_id")]
    Guid MessageId,

    [property: JsonPropertyName("total")]
    long Total,

    [property: JsonPropertyName("queued")]
    long Queued,

    [property: JsonPropertyName("processing")]
    long Processing,

    [property: JsonPropertyName("sent")]
    long Sent,

    [property: JsonPropertyName("delivered")]
    long Delivered,

    [property: JsonPropertyName("retry_scheduled")]
    long RetryScheduled,

    [property: JsonPropertyName("failed")]
    long Failed,

    [property: JsonPropertyName("expired")]
    long Expired);
