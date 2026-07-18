namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookAttempt
{
    private WebhookAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid WebhookEventId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string Outcome { get; private set; } = null!;
    public int? HttpStatusCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public long LatencyMilliseconds { get; private set; }
}
