namespace NotifyRail.Api.Features.Webhooks.Worker;

public sealed class WebhookWorkerOptions
{
    public const string SectionName = "WebhookWorker";
    public const int DefaultBatchSize = 1;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultMinimumRetryDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultMaximumRetryDelay = TimeSpan.FromHours(1);
    public static readonly TimeSpan DefaultClaimTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);
    public const double DefaultJitterRatio = 0.2;

    public string WorkerId { get; set; } = $"notifyrail-webhook-{Guid.NewGuid()}";
    public int BatchSize { get; set; } = DefaultBatchSize;
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;
    public TimeSpan BaseRetryDelay { get; set; } = DefaultBaseRetryDelay;
    public TimeSpan MinimumRetryDelay { get; set; } = DefaultMinimumRetryDelay;
    public TimeSpan MaximumRetryDelay { get; set; } = DefaultMaximumRetryDelay;
    public TimeSpan ClaimTimeout { get; set; } = DefaultClaimTimeout;
    public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;
    public double JitterRatio { get; set; } = DefaultJitterRatio;
}
