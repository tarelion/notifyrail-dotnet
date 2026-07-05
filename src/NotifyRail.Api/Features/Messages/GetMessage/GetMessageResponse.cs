using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.GetMessage;

public sealed record GetMessageResponse(
    [property: JsonPropertyName("message_id")]
    Guid MessageId,

    [property: JsonPropertyName("type")]
    string Type,

    [property: JsonPropertyName("channel")]
    string Channel,

    [property: JsonPropertyName("sender_title")]
    string SenderTitle,

    [property: JsonPropertyName("body")]
    string Body,

    [property: JsonPropertyName("scheduled_at")]
    DateTimeOffset? ScheduledAt,

    [property: JsonPropertyName("report_label")]
    string? ReportLabel,

    [property: JsonPropertyName("encoding")]
    string? Encoding,

    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt,

    [property: JsonPropertyName("updated_at")]
    DateTimeOffset UpdatedAt,

    [property: JsonPropertyName("deliveries")]
    GetMessageDeliverySummaryResponse Deliveries);

public sealed record GetMessageDeliverySummaryResponse(
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
