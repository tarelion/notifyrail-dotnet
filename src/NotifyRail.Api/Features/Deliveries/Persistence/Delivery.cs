using NotifyRail.Api.Features.Messages.Persistence;

namespace NotifyRail.Api.Features.Deliveries.Persistence;

public sealed class Delivery
{
    private Delivery()
    {
    }

    public static Delivery Create(
        Guid messageId,
        string recipient,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        return new Delivery
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Recipient = recipient,
            Status = "queued",
            AttemptCount = 0,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public Guid Id { get; private set; }

    public Guid MessageId { get; private set; }

    public Message Message { get; private set; } = null!;

    public string Recipient { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public int AttemptCount { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public string? ClaimedBy { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
