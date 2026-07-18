using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Outbox;

public sealed class DeliveryWebhookOutbox(NotifyRailDbContext dbContext)
{
    public async Task CreateDeliverySentAsync(
        Guid deliveryId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var source = await (
            from delivery in dbContext.Deliveries
            join message in dbContext.Messages on delivery.MessageId equals message.Id
            join endpoint in dbContext.WebhookEndpoints
                on message.ApiClientId equals endpoint.ApiClientId
            where delivery.Id == deliveryId && endpoint.IsEnabled
            select new
            {
                message.ApiClientId,
                WebhookEndpointId = endpoint.Id,
                MessageId = message.Id,
                DeliveryId = delivery.Id,
                delivery.Recipient,
            }).SingleOrDefaultAsync(cancellationToken);

        if (source is null)
        {
            return;
        }

        var sequence = await dbContext.WebhookEvents
            .Where(webhookEvent => webhookEvent.DeliveryId == deliveryId)
            .CountAsync(cancellationToken) + 1;
        dbContext.WebhookEvents.Add(WebhookEvent.CreateDeliverySent(
            source.ApiClientId,
            source.WebhookEndpointId,
            source.MessageId,
            source.DeliveryId,
            source.Recipient,
            sequence,
            occurredAt));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
