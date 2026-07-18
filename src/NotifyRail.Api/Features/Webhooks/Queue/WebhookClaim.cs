namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookClaim(
    Guid EventId,
    string WorkerId,
    int AttemptNumber);
