using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.InspectDeadWebhookEvent;

public sealed class DeadWebhookEventReader(NotifyRailDbContext dbContext)
{
    public async Task<ListDeadWebhookEventsResponse> ListAsync(
        CancellationToken cancellationToken)
    {
        var webhookEvents = await dbContext.WebhookEvents
            .AsNoTracking()
            .Where(webhookEvent => webhookEvent.Status == "dead")
            .OrderByDescending(webhookEvent => webhookEvent.UpdatedAt)
            .ThenByDescending(webhookEvent => webhookEvent.Id)
            .Select(webhookEvent => new DeadWebhookEventSummaryResponse(
                webhookEvent.Id,
                webhookEvent.ApiClientId,
                webhookEvent.MessageId,
                webhookEvent.DeliveryId,
                webhookEvent.Type,
                webhookEvent.Version,
                webhookEvent.Sequence,
                webhookEvent.OccurredAt,
                webhookEvent.Status,
                webhookEvent.AttemptCount,
                webhookEvent.AutomaticAttemptDeadlineAt,
                webhookEvent.CreatedAt,
                webhookEvent.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new ListDeadWebhookEventsResponse(webhookEvents);
    }

    public async Task<InspectDeadWebhookEventResponse?> ReadAsync(
        Guid webhookEventId,
        CancellationToken cancellationToken)
    {
        var webhookEvent = await dbContext.WebhookEvents
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == webhookEventId
                && candidate.Status == "dead")
            .Select(candidate => new DeadWebhookEventProjection(
                candidate.Id,
                candidate.ApiClientId,
                candidate.MessageId,
                candidate.DeliveryId,
                candidate.Type,
                candidate.Version,
                candidate.Sequence,
                candidate.OccurredAt,
                candidate.Status,
                candidate.AttemptCount,
                candidate.AutomaticAttemptDeadlineAt,
                candidate.CreatedAt,
                candidate.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (webhookEvent is null)
        {
            return null;
        }

        var attempts = await dbContext.WebhookAttempts
            .AsNoTracking()
            .Where(attempt => attempt.WebhookEventId == webhookEventId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .Select(attempt => new DeadWebhookAttemptResponse(
                attempt.AttemptNumber,
                attempt.Outcome,
                attempt.HttpStatusCode,
                attempt.ErrorCode,
                attempt.ErrorMessage,
                attempt.AttemptedAt,
                attempt.CompletedAt,
                attempt.LatencyMilliseconds))
            .ToListAsync(cancellationToken);

        return new InspectDeadWebhookEventResponse(
            webhookEvent.WebhookEventId,
            webhookEvent.ApiClientId,
            webhookEvent.MessageId,
            webhookEvent.DeliveryId,
            webhookEvent.Type,
            webhookEvent.Version,
            webhookEvent.Sequence,
            webhookEvent.OccurredAt,
            webhookEvent.Status,
            webhookEvent.AttemptCount,
            webhookEvent.AutomaticAttemptDeadlineAt,
            webhookEvent.CreatedAt,
            webhookEvent.UpdatedAt,
            attempts);
    }

    private sealed record DeadWebhookEventProjection(
        Guid WebhookEventId,
        Guid ApiClientId,
        Guid MessageId,
        Guid DeliveryId,
        string Type,
        int Version,
        int Sequence,
        DateTimeOffset OccurredAt,
        string Status,
        int AttemptCount,
        DateTimeOffset? AutomaticAttemptDeadlineAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
