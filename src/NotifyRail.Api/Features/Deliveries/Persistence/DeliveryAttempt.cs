namespace NotifyRail.Api.Features.Deliveries.Persistence;

public sealed class DeliveryAttempt
{
    private DeliveryAttempt()
    {
    }

    public static DeliveryAttempt Create(
        Guid deliveryId,
        int attemptNumber,
        string provider,
        string outcome,
        DateTimeOffset attemptedAt,
        string? providerMessageId = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            AttemptNumber = attemptNumber,
            Provider = provider,
            Outcome = outcome,
            ProviderMessageId = providerMessageId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            AttemptedAt = attemptedAt,
        };
    }

    public Guid Id { get; private set; }

    public Guid DeliveryId { get; private set; }

    public Delivery Delivery { get; private set; } = null!;

    public int AttemptNumber { get; private set; }

    public string Provider { get; private set; } = null!;

    public string Outcome { get; private set; } = null!;

    public string? ProviderMessageId { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }
}
