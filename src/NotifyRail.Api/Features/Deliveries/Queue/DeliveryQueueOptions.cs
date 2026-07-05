namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed class DeliveryQueueOptions
{
    public const string SectionName = "DeliveryQueue";

    public static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromMinutes(1);

    public TimeSpan BaseRetryDelay { get; set; } = DefaultBaseRetryDelay;
}
