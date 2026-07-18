namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookResult(
    bool Succeeded,
    int? HttpStatusCode,
    long LatencyMilliseconds,
    string? ErrorCode = null,
    string? ErrorMessage = null);
