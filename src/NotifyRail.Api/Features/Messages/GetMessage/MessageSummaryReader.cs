using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Messages.GetMessage;

public sealed class MessageSummaryReader(NotifyRailDbContext dbContext)
{
    public async Task<GetMessageResponse?> ReadAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.Id == messageId)
            .Select(message => new
            {
                message.Id,
                message.Type,
                message.Channel,
                message.SenderTitle,
                message.Body,
                message.ScheduledAt,
                message.ReportLabel,
                message.Encoding,
                message.CreatedAt,
                message.UpdatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (message is null)
        {
            return null;
        }

        var summary = await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.MessageId == messageId)
            .GroupBy(delivery => delivery.MessageId)
            .Select(deliveries => new GetMessageDeliverySummaryResponse(
                deliveries.LongCount(),
                deliveries.LongCount(delivery => delivery.Status == "queued"),
                deliveries.LongCount(delivery => delivery.Status == "processing"),
                deliveries.LongCount(delivery => delivery.Status == "sent"),
                deliveries.LongCount(delivery => delivery.Status == "delivered"),
                deliveries.LongCount(delivery => delivery.Status == "retry_scheduled"),
                deliveries.LongCount(delivery => delivery.Status == "failed"),
                deliveries.LongCount(delivery => delivery.Status == "expired")))
            .SingleOrDefaultAsync(cancellationToken);

        return new GetMessageResponse(
            message.Id,
            message.Type,
            message.Channel,
            message.SenderTitle,
            message.Body,
            message.ScheduledAt,
            message.ReportLabel,
            message.Encoding,
            message.CreatedAt,
            message.UpdatedAt,
            summary ?? new GetMessageDeliverySummaryResponse(
                Total: 0,
                Queued: 0,
                Processing: 0,
                Sent: 0,
                Delivered: 0,
                RetryScheduled: 0,
                Failed: 0,
                Expired: 0));
    }
}
