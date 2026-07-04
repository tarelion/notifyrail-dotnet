namespace NotifyRail.Api.Features.Deliveries.Worker;

public sealed class DeliveryWorkerOptions
{
    public const string SectionName = "DeliveryWorker";

    public string WorkerId { get; set; } = "notifyrail-worker";

    public int BatchSize { get; set; } = 10;
}
