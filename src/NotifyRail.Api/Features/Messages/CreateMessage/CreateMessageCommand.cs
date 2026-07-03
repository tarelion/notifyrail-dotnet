namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed record CreateMessageCommand(
    string Type,
    string Channel,
    string SenderTitle,
    string Body,
    IReadOnlyList<string> Recipients,
    string IdempotencyKey,
    DateTimeOffset? ScheduledAt,
    string? ReportLabel,
    string? Encoding);
