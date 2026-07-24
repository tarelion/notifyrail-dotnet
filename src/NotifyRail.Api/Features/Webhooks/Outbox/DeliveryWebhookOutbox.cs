using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Webhooks.Outbox;

public sealed class DeliveryWebhookOutbox(
    NotifyRailDbContext dbContext,
    ILogger<DeliveryWebhookOutbox> logger)
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
        using var activity = NotifyRailTelemetry.ActivitySource.StartActivity(
            NotifyRailTelemetry.WebhookEventCreateActivity,
            ActivityKind.Producer);
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
            activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "no_active_endpoint");
            return;
        }
        NotifyRailTelemetry.SetCorrelation(
            activity,
            new TelemetryCorrelation(
                source.ApiClientId,
                source.MessageId,
                source.DeliveryId,
                SourceTraceParent: null),
            source.Recipient);

        var lastSequence = await dbContext.WebhookEvents
            .Where(webhookEvent => webhookEvent.DeliveryId == deliveryId)
            .MaxAsync(webhookEvent => (int?)webhookEvent.Sequence, cancellationToken) ?? 0;
        var webhookEvent = WebhookEvent.CreateDeliveryStateChanged(
            source.ApiClientId,
            source.WebhookEndpointId,
            source.MessageId,
            source.DeliveryId,
            source.Recipient,
            status,
            lastSequence + 1,
            occurredAt,
            NotifyRailTelemetry.CaptureCurrentTraceParent());
        dbContext.WebhookEvents.Add(webhookEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        activity?.SetTag(
            NotifyRailTelemetry.WebhookEventIdTag,
            webhookEvent.Id.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.WebhookEventTypeTag,
            webhookEvent.Type);
        activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "created");
        logger.LogInformation(
            "Created Webhook Event {notifyrail.webhook_event.id} for Delivery " +
            "{notifyrail.delivery.id}, Message {notifyrail.message.id}, and API Client " +
            "{notifyrail.api_client.id} with type {notifyrail.webhook_event.type} " +
            "for {notifyrail.recipient.masked}",
            webhookEvent.Id,
            source.DeliveryId,
            source.MessageId,
            source.ApiClientId,
            webhookEvent.Type,
            NotifyRailTelemetry.MaskRecipient(source.Recipient));
    }
}
