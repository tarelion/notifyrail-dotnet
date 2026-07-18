namespace NotifyRail.Api.Features.Webhooks.Queue;

public enum WebhookOutcome
{
    Succeeded,
    RetryableFailure,
    PermanentFailure,
}
