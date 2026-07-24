namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookRecordResult(
    Guid WebhookAttemptId,
    string EventStatus);
