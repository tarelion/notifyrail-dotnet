using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed class MessageIntake
{
    private const string IdempotencyKeyUniqueConstraint = "messages_idempotency_key_key";

    private readonly NotifyRailDbContext _dbContext;

    public MessageIntake(NotifyRailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateMessageOutcome> CreateAsync(
        CreateMessageCommand command,
        CancellationToken cancellationToken)
    {
        var createdAt = TruncateToMicrosecond(DateTimeOffset.UtcNow);

        var message = Message.Create(
            command.Type,
            command.Channel,
            command.SenderTitle,
            command.Body,
            command.IdempotencyKey,
            createdAt,
            command.ScheduledAt,
            command.ReportLabel,
            command.Encoding);

        var deliveries = command.Recipients
            .Select(recipient => Delivery.Create(message.Id, recipient, createdAt))
            .ToArray();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Messages.Add(message);
        _dbContext.Deliveries.AddRange(deliveries);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            return await ReplayExistingMessageAsync(command, cancellationToken);
        }

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
        CreateMessageCommand command,
        CancellationToken cancellationToken)
    {
        var existingMessage = await _dbContext.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.IdempotencyKey == command.IdempotencyKey,
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
            && TruncateToMicrosecond(left.Value) == TruncateToMicrosecond(right.Value);
    }

    private static DateTimeOffset TruncateToMicrosecond(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        var ticks = utcValue.Ticks - utcValue.Ticks % 10;

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
