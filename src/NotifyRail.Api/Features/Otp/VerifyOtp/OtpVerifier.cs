using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed class OtpVerifier(
    NotifyRailDbContext dbContext,
    OtpCode otpCode,
    TimeProvider timeProvider)
{
    public async Task<VerifyOtpOutcome> VerifyAsync(
        Guid otpId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var challenges = await dbContext.OtpChallenges
            .FromSqlInterpolated(
                $"SELECT * FROM otp_challenges WHERE id = {otpId} FOR UPDATE")
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
