using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Webhooks.Outbox;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed class MockProviderCallbackHandler(
    NotifyRailDbContext dbContext,
    DeliveryWebhookOutbox webhookOutbox,
    TimeProvider timeProvider,
    ILogger<MockProviderCallbackHandler> logger)
{
    public async Task<MockProviderCallbackResponse?> ApplyAsync(
        string providerMessageId,
        string status,
        CancellationToken cancellationToken)
    {
        var updatedAt = timeProvider.GetUtcNow();
        var source = await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.ProviderMessageId == providerMessageId)
            .Select(delivery => new
            {
                DeliveryId = delivery.Id,
                delivery.MessageId,
                delivery.Message.ApiClientId,
                delivery.SourceTraceParent,
                delivery.Recipient,
            })
            .SingleOrDefaultAsync(cancellationToken);
        using var activity = NotifyRailTelemetry.StartLinkedActivity(
            NotifyRailTelemetry.ProviderCallbackActivity,
            ActivityKind.Consumer,
            source?.SourceTraceParent);
        activity?.SetTag(
            NotifyRailTelemetry.ApiClientIdTag,
            source?.ApiClientId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.MessageIdTag,
            source?.MessageId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.DeliveryIdTag,
            source?.DeliveryId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.RecipientTag,
            source is null
                ? null
                : NotifyRailTelemetry.MaskRecipient(source.Recipient));
        activity?.SetTag(NotifyRailTelemetry.OutcomeTag, status);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.Deliveries
            .Where(delivery =>
                delivery.ProviderMessageId == providerMessageId &&
                delivery.Status == "sent")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, status)
                    .SetProperty(delivery => delivery.UpdatedAt, updatedAt),
                cancellationToken);

        if (updated == 1)
        {
            var deliveryId = await dbContext.Deliveries
                .Where(delivery => delivery.ProviderMessageId == providerMessageId)
                .Select(delivery => delivery.Id)
                .SingleAsync(cancellationToken);
            if (status == "delivered")
            {
                await webhookOutbox.CreateDeliveryDeliveredAsync(
                    deliveryId,
                    updatedAt,
                    cancellationToken);
            }
            else
            {
                await webhookOutbox.CreateDeliveryFailedAsync(
                    deliveryId,
                    updatedAt,
                    cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        if (source is not null && updated == 1)
        {
            logger.LogInformation(
                "Applied Provider Callback to Delivery {notifyrail.delivery.id} " +
                "for Message {notifyrail.message.id} and API Client " +
                "{notifyrail.api_client.id} as {notifyrail.outcome} for " +
                "{notifyrail.recipient.masked}",
                source.DeliveryId,
                source.MessageId,
                source.ApiClientId,
                status,
                NotifyRailTelemetry.MaskRecipient(source.Recipient));
        }

        return await dbContext.Deliveries
            .AsNoTracking()
            .Where(delivery => delivery.ProviderMessageId == providerMessageId)
            .Select(delivery => new MockProviderCallbackResponse(
                delivery.Id,
                delivery.ProviderMessageId!,
                delivery.Status,
                delivery.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
