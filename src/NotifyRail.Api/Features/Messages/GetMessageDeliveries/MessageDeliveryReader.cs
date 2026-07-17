using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Messages.GetMessageDeliveries;

public sealed class MessageDeliveryReader(NotifyRailDbContext dbContext)
{
    public async Task<GetMessageDeliveriesResponse?> ReadAsync(
        Guid apiClientId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var messageExists = await dbContext.Messages
            .AsNoTracking()
            .AnyAsync(
                message => message.ApiClientId == apiClientId && message.Id == messageId,
                cancellationToken);
        if (!messageExists)
        {
            return null;
        }

        var deliveries = await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.MessageId == messageId)
            .OrderBy(delivery => delivery.CreatedAt)
            .ThenBy(delivery => delivery.Id)
            .Select(delivery => new GetMessageDeliveryResponse(
                delivery.Id,
                delivery.Recipient,
                delivery.Status,
                delivery.AttemptCount,
                delivery.NextAttemptAt,
                delivery.ProviderMessageId,
                delivery.ExpiresAt,
                delivery.CreatedAt,
                delivery.UpdatedAt,
                dbContext.DeliveryAttempts
                    .Where(attempt => attempt.DeliveryId == delivery.Id)
                    .OrderBy(attempt => attempt.AttemptNumber)
                    .Select(attempt => new GetMessageDeliveryAttemptResponse(
                        attempt.AttemptNumber,
                        attempt.Provider,
                        attempt.Outcome,
                        attempt.ProviderMessageId,
                        attempt.ErrorCode,
                        attempt.ErrorMessage,
                        attempt.AttemptedAt))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new GetMessageDeliveriesResponse(messageId, deliveries);
    }
}
