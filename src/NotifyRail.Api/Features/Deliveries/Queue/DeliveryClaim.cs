namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed class DeliveryClaim
{
    internal DeliveryClaim(Guid deliveryId, string workerId, int attemptNumber)
    {
        DeliveryId = deliveryId;
        WorkerId = workerId;
        AttemptNumber = attemptNumber;
    }

    internal Guid DeliveryId { get; }

    internal string WorkerId { get; }

    internal int AttemptNumber { get; }
}
