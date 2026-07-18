namespace NotifyRail.Api.Features.Webhooks.Worker;

public sealed class WebhookWorkerOptions
{
    public const string SectionName = "WebhookWorker";
    public const int DefaultBatchSize = 1;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    public string WorkerId { get; set; } = $"notifyrail-webhook-{Guid.NewGuid()}";
    public int BatchSize { get; set; } = DefaultBatchSize;
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;
}
