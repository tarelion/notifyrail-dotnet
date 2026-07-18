using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Webhooks.Outbox;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed class MockProviderCallbackHandler(
    NotifyRailDbContext dbContext,
    DeliveryWebhookOutbox webhookOutbox,
    TimeProvider timeProvider)
{
    public async Task<MockProviderCallbackResponse?> ApplyAsync(
        string providerMessageId,
        string status,
        CancellationToken cancellationToken)
    {
        var updatedAt = timeProvider.GetUtcNow();

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
            await webhookOutbox.CreateDeliveryEventAsync(
                deliveryId,
                status,
                updatedAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

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
