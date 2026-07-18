namespace NotifyRail.Api.Features.Webhooks.Queue;

public abstract record WebhookRetryAfter
{
    private WebhookRetryAfter()
    {
    }

    public sealed record Relative(TimeSpan Delay) : WebhookRetryAfter;

    public sealed record Absolute(DateTimeOffset RetryAt) : WebhookRetryAfter;
}
