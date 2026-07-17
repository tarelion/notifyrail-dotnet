using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Messages.GetMessageReport;

public sealed class MessageReportReader(NotifyRailDbContext dbContext)
{
    public async Task<GetMessageReportResponse?> ReadAsync(
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

        var report = await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.MessageId == messageId)
            .GroupBy(delivery => delivery.MessageId)
            .Select(deliveries => new GetMessageReportResponse(
                messageId,
                deliveries.LongCount(),
                deliveries.LongCount(delivery => delivery.Status == "queued"),
                deliveries.LongCount(delivery => delivery.Status == "processing"),
                deliveries.LongCount(delivery => delivery.Status == "sent"),
                deliveries.LongCount(delivery => delivery.Status == "delivered"),
                deliveries.LongCount(delivery => delivery.Status == "retry_scheduled"),
                deliveries.LongCount(delivery => delivery.Status == "failed"),
                deliveries.LongCount(delivery => delivery.Status == "expired")))
            .SingleOrDefaultAsync(cancellationToken);

        return report ?? new GetMessageReportResponse(
            messageId,
            Total: 0,
            Queued: 0,
            Processing: 0,
            Sent: 0,
            Delivered: 0,
            RetryScheduled: 0,
            Failed: 0,
            Expired: 0);
    }
}
