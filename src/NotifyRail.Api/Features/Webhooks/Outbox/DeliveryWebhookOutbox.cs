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
        await CreateDeliveryEventAsync(
            deliveryId,
            DeliveryWebhookEventStatus.Sent,
            occurredAt,
            cancellationToken);
    }

    public async Task CreateDeliveryDeliveredAsync(
        Guid deliveryId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await CreateDeliveryEventAsync(
            deliveryId,
            DeliveryWebhookEventStatus.Delivered,
            occurredAt,
            cancellationToken);
    }

    public async Task CreateDeliveryFailedAsync(
        Guid deliveryId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await CreateDeliveryEventAsync(
            deliveryId,
            DeliveryWebhookEventStatus.Failed,
            occurredAt,
            cancellationToken);
    }

    public async Task CreateDeliveryExpiredAsync(
        Guid deliveryId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await CreateDeliveryEventAsync(
            deliveryId,
            DeliveryWebhookEventStatus.Expired,
            occurredAt,
            cancellationToken);
    }

    private async Task CreateDeliveryEventAsync(
        Guid deliveryId,
        DeliveryWebhookEventStatus status,
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

        var lastSequence = await dbContext.WebhookEvents
            .Where(webhookEvent => webhookEvent.DeliveryId == deliveryId)
            .MaxAsync(webhookEvent => (int?)webhookEvent.Sequence, cancellationToken) ?? 0;
        dbContext.WebhookEvents.Add(WebhookEvent.CreateDeliveryStateChanged(
            source.ApiClientId,
            source.WebhookEndpointId,
            source.MessageId,
            source.DeliveryId,
            source.Recipient,
            status,
            lastSequence + 1,
            occurredAt));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
