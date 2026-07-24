namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed record DeliveryJob(
    DeliveryClaim Claim,
    ProviderRequest Request,
    Guid ApiClientId,
    Guid MessageId,
    string? SourceTraceParent);
