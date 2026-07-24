using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed class MessageIntake
{
    private const string IdempotencyKeyUniqueConstraint =
        "messages_api_client_id_idempotency_key_key";

    private readonly NotifyRailDbContext _dbContext;
    private readonly ILogger<MessageIntake> _logger;

    public MessageIntake(
        NotifyRailDbContext dbContext,
        ILogger<MessageIntake> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CreateMessageOutcome> CreateAsync(
        Guid apiClientId,
        CreateMessageCommand command,
        CancellationToken cancellationToken)
    {
        using var activity = NotifyRailTelemetry.ActivitySource.StartActivity(
            NotifyRailTelemetry.MessageIntakeActivity,
            ActivityKind.Producer);
        activity?.SetTag(NotifyRailTelemetry.ApiClientIdTag, apiClientId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.RecipientTag,
            string.Join(
                ',',
                command.Recipients.Select(NotifyRailTelemetry.MaskRecipient)));
        var sourceTraceParent = NotifyRailTelemetry.CaptureCurrentTraceParent();

        var createdAt = PostgresTimestamp.Normalize(DateTimeOffset.UtcNow);
        var scheduledAt = command.ScheduledAt is null
            ? (DateTimeOffset?)null
            : PostgresTimestamp.Normalize(command.ScheduledAt.Value);

        var message = Message.Create(
            apiClientId,
            command.Type,
            command.Channel,
            command.SenderTitle,
            command.Body,
            command.IdempotencyKey,
            createdAt,
            scheduledAt,
            command.ReportLabel,
            command.Encoding);

        var deliveries = command.Recipients
            .Select(recipient => Delivery.Create(
                message.Id,
                recipient,
                createdAt,
                sourceTraceParent: sourceTraceParent))
            .ToArray();
        activity?.SetTag(NotifyRailTelemetry.MessageIdTag, message.Id.ToString());
        activity?.SetTag(NotifyRailTelemetry.DeliveryCountTag, deliveries.Length);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Messages.Add(message);
        _dbContext.Deliveries.AddRange(deliveries);

        // The database constraint arbitrates concurrent requests with the same
        // idempotency key. The loser rolls back and replays the committed winner.
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);

            // Detach the rejected message and delivery graph before querying the
            // committed winner through this DbContext.
            _dbContext.ChangeTracker.Clear();

            var replay = await ReplayExistingMessageAsync(
                apiClientId,
                command,
                cancellationToken);
            activity?.SetTag(
                NotifyRailTelemetry.MessageIdTag,
                replay.Response?.MessageId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.OutcomeTag,
                replay.Kind.ToString());
            return replay;
        }

        activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "Accepted");
        _logger.LogInformation(
            "Accepted Message {notifyrail.message.id} for API Client " +
            "{notifyrail.api_client.id} with {notifyrail.delivery.count} Deliveries " +
            "for {notifyrail.recipient.masked}",
            message.Id,
            apiClientId,
            deliveries.Length,
            string.Join(
                ',',
                command.Recipients.Select(NotifyRailTelemetry.MaskRecipient)));
        return CreateMessageOutcome.Accepted(new CreateMessageResponse(
            message.Id,
            deliveries.Length,
            message.CreatedAt));
    }

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == IdempotencyKeyUniqueConstraint;
    }

    private async Task<CreateMessageOutcome> ReplayExistingMessageAsync(
        Guid apiClientId,
        CreateMessageCommand command,
        CancellationToken cancellationToken)
    {
        var existingMessage = await _dbContext.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.ApiClientId == apiClientId
                    && message.IdempotencyKey == command.IdempotencyKey,
                cancellationToken);

        if (existingMessage is null)
        {
            throw new InvalidOperationException(
                "Could not load existing message for idempotency replay.");
        }

        var existingRecipients = await _dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.MessageId == existingMessage.Id)
            .OrderBy(delivery => delivery.Recipient)
            .Select(delivery => delivery.Recipient)
            .ToArrayAsync(cancellationToken);

        if (!MessageMatchesCommand(existingMessage, existingRecipients, command))
        {
            return CreateMessageOutcome.IdempotencyConflict(
                "idempotency key is already used with different content");
        }

        return CreateMessageOutcome.Accepted(new CreateMessageResponse(
            existingMessage.Id,
            existingRecipients.Length,
            existingMessage.CreatedAt));
    }

    private static bool MessageMatchesCommand(
        Message message,
        IReadOnlyCollection<string> recipients,
        CreateMessageCommand command)
    {
        return message.Type == command.Type
            && message.Channel == command.Channel
            && message.SenderTitle == command.SenderTitle
            && message.Body == command.Body
            && message.ReportLabel == command.ReportLabel
            && message.Encoding == command.Encoding
            && SameInstant(message.ScheduledAt, command.ScheduledAt)
            && RecipientsMatch(recipients, command.Recipients);
    }

    private static bool RecipientsMatch(
        IReadOnlyCollection<string> existingRecipients,
        IReadOnlyCollection<string> requestedRecipients)
    {
        return existingRecipients
            .Order(StringComparer.Ordinal)
            .SequenceEqual(requestedRecipients.Order(StringComparer.Ordinal));
    }

    private static bool SameInstant(DateTimeOffset? left, DateTimeOffset? right)
    {
        return left is null && right is null
            || left is not null
            && right is not null
            && PostgresTimestamp.Normalize(left.Value) == PostgresTimestamp.Normalize(right.Value);
    }
}
