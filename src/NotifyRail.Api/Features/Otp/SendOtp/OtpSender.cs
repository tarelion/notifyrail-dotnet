using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Features.Otp.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed class OtpSender(
    NotifyRailDbContext dbContext,
    OtpCode otpCode,
    IOptions<OtpOptions> options,
    TimeProvider timeProvider)
{
    private const string MessageBody = "Your verification code is ready.";
    private const string IdempotencyKeyUniqueConstraint =
        "messages_api_client_id_idempotency_key_key";

    public async Task<SendOtpOutcome> SendAsync(
        Guid apiClientId,
        string recipient,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var createdAt = PostgresTimestamp.Normalize(timeProvider.GetUtcNow());
        var expiresAt = createdAt.Add(options.Value.Ttl);
        var challengeId = Guid.NewGuid();
        var debugCode = otpCode.Derive(challengeId);

        var message = Message.Create(
            apiClientId,
            type: "otp",
            channel: "sms",
            senderTitle: options.Value.SenderTitle,
            body: MessageBody,
            idempotencyKey,
            createdAt);
        var delivery = Delivery.Create(
            message.Id,
            recipient,
            createdAt,
            expiresAt);
        var challenge = OtpChallenge.Create(
            challengeId,
            message.Id,
            recipient,
            otpCode.Hash(challengeId, debugCode),
            expiresAt,
            options.Value.MaxAttempts,
            createdAt);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Messages.Add(message);
        dbContext.Deliveries.Add(delivery);
        dbContext.OtpChallenges.Add(challenge);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return await ReplayExistingAsync(
                apiClientId,
                recipient,
                idempotencyKey,
                cancellationToken);
        }

        return SendOtpOutcome.Accepted(new SendOtpResponse(
            challenge.Id,
            message.Id,
            challenge.ExpiresAt,
            debugCode));
    }

    private async Task<SendOtpOutcome> ReplayExistingAsync(
        Guid apiClientId,
        string recipient,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.OtpChallenges
            .AsNoTracking()
            .Where(challenge => challenge.Message.ApiClientId == apiClientId
                && challenge.Message.IdempotencyKey == idempotencyKey)
            .Select(challenge => new
            {
                Challenge = challenge,
                challenge.Message.Type,
                challenge.Message.Channel,
                challenge.Message.SenderTitle,
                challenge.Message.Body,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return SendOtpOutcome.IdempotencyConflict(
                "idempotency key is already used with different content");
        }

        if (existing.Type != "otp" ||
            existing.Channel != "sms" ||
            existing.SenderTitle != options.Value.SenderTitle ||
            existing.Body != MessageBody ||
            existing.Challenge.Recipient != recipient)
        {
            return SendOtpOutcome.IdempotencyConflict(
                "idempotency key is already used with different content");
        }

        return SendOtpOutcome.Accepted(new SendOtpResponse(
            existing.Challenge.Id,
            existing.Challenge.MessageId,
            existing.Challenge.ExpiresAt,
            otpCode.Derive(existing.Challenge.Id)));
    }

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == IdempotencyKeyUniqueConstraint;
    }

}
