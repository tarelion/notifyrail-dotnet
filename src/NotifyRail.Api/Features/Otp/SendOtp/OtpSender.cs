using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Features.Otp.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed class OtpSender(
    NotifyRailDbContext dbContext,
    OtpCode otpCode,
    IOptions<OtpOptions> options,
    TimeProvider timeProvider,
    ILogger<OtpSender> logger)
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
        using var activity = NotifyRailTelemetry.ActivitySource.StartActivity(
            NotifyRailTelemetry.MessageIntakeActivity,
            ActivityKind.Producer);
        activity?.SetTag(
            NotifyRailTelemetry.ApiClientIdTag,
            apiClientId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.RecipientTag,
            NotifyRailTelemetry.MaskRecipient(recipient));
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
            expiresAt,
            NotifyRailTelemetry.CaptureCurrentTraceParent());
        activity?.SetTag(
            NotifyRailTelemetry.MessageIdTag,
            message.Id.ToString());
        activity?.SetTag(NotifyRailTelemetry.DeliveryCountTag, 1);
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

            var replay = await ReplayExistingAsync(
                apiClientId,
                recipient,
                idempotencyKey,
                cancellationToken);
            activity?.SetTag(
                NotifyRailTelemetry.MessageIdTag,
                replay.Response?.MessageId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.OutcomeTag,
                replay.Response is null ? "IdempotencyConflict" : "Accepted");
            return replay;
        }

        activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "Accepted");
        logger.LogInformation(
            "Accepted OTP Message {notifyrail.message.id} for API Client " +
            "{notifyrail.api_client.id} with {notifyrail.delivery.count} Delivery " +
            "for {notifyrail.recipient.masked}",
            message.Id,
            apiClientId,
            1,
            NotifyRailTelemetry.MaskRecipient(recipient));
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
