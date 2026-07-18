namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookResult(
    WebhookOutcome Outcome,
    int? HttpStatusCode,
    long LatencyMilliseconds,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    TimeSpan? RetryAfter = null);
