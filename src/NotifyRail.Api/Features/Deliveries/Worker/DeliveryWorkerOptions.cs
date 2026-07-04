namespace NotifyRail.Api.Features.Deliveries.Worker;

public sealed class DeliveryWorkerOptions
{
    public const string SectionName = "DeliveryWorker";

    public const int DefaultBatchSize = 1;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    public string WorkerId { get; set; } = $"notifyrail-{Guid.NewGuid()}";

    public int BatchSize { get; set; } = DefaultBatchSize;

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;
}
