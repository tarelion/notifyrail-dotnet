using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed record CreateMessageRequest(
    [property: JsonPropertyName("type")]
    string Type,

    [property: JsonPropertyName("channel")]
    string Channel,

    [property: JsonPropertyName("sender_title")]
    string SenderTitle,

    [property: JsonPropertyName("body")]
    string Body,

    [property: JsonPropertyName("recipients")]
    IReadOnlyList<string> Recipients,

    [property: JsonPropertyName("idempotency_key")]
    string IdempotencyKey,

    [property: JsonPropertyName("scheduled_at")]
    DateTimeOffset? ScheduledAt = null,

    [property: JsonPropertyName("report_label")]
    string? ReportLabel = null,

    [property: JsonPropertyName("encoding")]
    string? Encoding = null);
