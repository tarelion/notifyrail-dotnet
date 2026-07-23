using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.ReplayDeadWebhookEvent;

public sealed class WebhookEventReplayer(NotifyRailDbContext dbContext)
{
    public async Task<ReplayDeadWebhookEventResponse?> ReplayAsync(
        Guid webhookEventId,
        DateTimeOffset replayedAt,
        CancellationToken cancellationToken)
    {
        var webhookEvent = await dbContext.WebhookEvents
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == webhookEventId
                && candidate.Status == "dead")
            .Select(candidate => new ReplayProjection(
                candidate.Id,
                candidate.ApiClientId,
                candidate.MessageId,
                candidate.DeliveryId,
                candidate.Type,
                candidate.Version,
                candidate.Sequence,
                candidate.OccurredAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (webhookEvent is null)
        {
            return null;
        }

        var updated = await dbContext.WebhookEvents
            .Where(candidate =>
                candidate.Id == webhookEventId
                && candidate.Status == "dead")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, "pending")
                    .SetProperty(
                        candidate => candidate.NextAttemptAt,
                        (DateTimeOffset?)null)
                    .SetProperty(
                        candidate => candidate.ClaimedAt,
                        (DateTimeOffset?)null)
                    .SetProperty(candidate => candidate.ClaimedBy, (string?)null)
                    .SetProperty(
                        candidate => candidate.SucceededAt,
                        (DateTimeOffset?)null)
                    .SetProperty(candidate => candidate.UpdatedAt, replayedAt),
                cancellationToken);
        if (updated == 0)
        {
            return null;
        }

        return new ReplayDeadWebhookEventResponse(
            webhookEvent.WebhookEventId,
            webhookEvent.ApiClientId,
            webhookEvent.MessageId,
            webhookEvent.DeliveryId,
            webhookEvent.Type,
            webhookEvent.Version,
            webhookEvent.Sequence,
            webhookEvent.OccurredAt,
            "pending",
            replayedAt);
    }

    private sealed record ReplayProjection(
        Guid WebhookEventId,
        Guid ApiClientId,
        Guid MessageId,
        Guid DeliveryId,
        string Type,
        int Version,
        int Sequence,
        DateTimeOffset OccurredAt);
}
