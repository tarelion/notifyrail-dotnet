using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public static class CreateMessageEndpoint
{
    private const string IdempotencyKeyUniqueConstraint = "messages_idempotency_key_key";

    public static IEndpointRouteBuilder MapCreateMessageEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/messages", CreateAsync)
            .WithName("CreateMessage")
            .Produces<CreateMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateMessageRequest request,
        NotifyRailDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalization = CreateMessageRequestNormalizer.Normalize(request);
        if (!normalization.IsSuccess)
        {
            return Results.BadRequest(new CreateMessageErrorResponse(normalization.Error!));
        }

        var command = normalization.Command!;
        var createdAt = DateTimeOffset.UtcNow;

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
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Messages.Add(message);
        dbContext.Deliveries.AddRange(deliveries);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return await ReplayExistingMessageAsync(command, dbContext, cancellationToken);
        }

        var response = new CreateMessageResponse(
            message.Id,
            deliveries.Length,
            message.CreatedAt);

        return Results.Accepted($"/messages/{message.Id}", response);
    }

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == IdempotencyKeyUniqueConstraint;
    }

    private static async Task<IResult> ReplayExistingMessageAsync(
        CreateMessageCommand command,
        NotifyRailDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existingMessage = await dbContext.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.IdempotencyKey == command.IdempotencyKey,
                cancellationToken);

        if (existingMessage is null)
        {
            return Results.Problem(
                "could not load existing message for idempotency replay",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var existingRecipients = await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.MessageId == existingMessage.Id)
            .OrderBy(delivery => delivery.Recipient)
            .Select(delivery => delivery.Recipient)
            .ToArrayAsync(cancellationToken);

        if (!MessageMatchesCommand(existingMessage, existingRecipients, command))
        {
            return Results.Conflict(new CreateMessageErrorResponse(
                "idempotency key is already used with different content"));
        }

        var response = new CreateMessageResponse(
            existingMessage.Id,
            existingRecipients.Length,
            existingMessage.CreatedAt);

        return Results.Accepted($"/messages/{existingMessage.Id}", response);
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
