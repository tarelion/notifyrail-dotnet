using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed class OtpVerifier(
    NotifyRailDbContext dbContext,
    OtpCode otpCode,
    TimeProvider timeProvider)
{
    public async Task<VerifyOtpOutcome> VerifyAsync(
        Guid apiClientId,
        Guid otpId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var challenges = await dbContext.OtpChallenges
            .FromSqlInterpolated(
                $"""
                SELECT challenge.*
                FROM otp_challenges AS challenge
                WHERE challenge.id = {otpId}
                  AND EXISTS (
                      SELECT 1
                      FROM messages AS message
                      WHERE message.id = challenge.message_id
                        AND message.api_client_id = {apiClientId})
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
        var challenge = challenges.SingleOrDefault();
        if (challenge is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VerifyOtpOutcome(VerifyOtpOutcomeKind.NotFound, null);
        }

        var now = PostgresTimestamp.Normalize(timeProvider.GetUtcNow());
        if (challenge.VerifiedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VerifyOtpOutcome(VerifyOtpOutcomeKind.AlreadyVerified, null);
        }
        if (now >= challenge.ExpiresAt)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VerifyOtpOutcome(VerifyOtpOutcomeKind.Expired, null);
        }
        if (challenge.FailedAttemptCount >= challenge.MaxAttempts)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VerifyOtpOutcome(
                VerifyOtpOutcomeKind.AttemptLimitExceeded,
                null,
                AttemptsRemaining: 0);
        }
        if (!otpCode.Matches(challenge.Id, code, challenge.CodeHash))
        {
            challenge.RecordFailedAttempt(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var remaining = challenge.MaxAttempts - challenge.FailedAttemptCount;
            return new VerifyOtpOutcome(
                remaining == 0
                    ? VerifyOtpOutcomeKind.AttemptLimitExceeded
                    : VerifyOtpOutcomeKind.IncorrectCode,
                null,
                remaining);
        }

        challenge.MarkVerified(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new VerifyOtpOutcome(
            VerifyOtpOutcomeKind.Verified,
            new VerifyOtpResponse(challenge.Id, "verified", now));
    }

}
