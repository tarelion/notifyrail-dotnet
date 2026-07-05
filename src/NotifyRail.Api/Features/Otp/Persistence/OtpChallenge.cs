using NotifyRail.Api.Features.Messages.Persistence;

namespace NotifyRail.Api.Features.Otp.Persistence;

public sealed class OtpChallenge
{
    private OtpChallenge()
    {
    }

    public static OtpChallenge Create(
        Guid id,
        Guid messageId,
        string recipient,
        byte[] codeHash,
        DateTimeOffset expiresAt,
        int maxAttempts,
        DateTimeOffset createdAt)
    {
        return new OtpChallenge
        {
            Id = id,
            MessageId = messageId,
            Recipient = recipient,
            CodeHash = codeHash,
            ExpiresAt = expiresAt,
            FailedAttemptCount = 0,
            MaxAttempts = maxAttempts,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public Guid Id { get; private set; }

    public Guid MessageId { get; private set; }

    public Message Message { get; private set; } = null!;

    public string Recipient { get; private set; } = null!;

    public byte[] CodeHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkVerified(DateTimeOffset verifiedAt)
    {
        VerifiedAt = verifiedAt;
        UpdatedAt = verifiedAt;
    }

    public void RecordFailedAttempt(DateTimeOffset attemptedAt)
    {
        FailedAttemptCount++;
        UpdatedAt = attemptedAt;
    }
}
