using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Webhooks.ReplayDeadWebhookEvent;

public sealed class WebhookEventReplayer(
    NotifyRailDbContext dbContext,
    ILogger<WebhookEventReplayer> logger)
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
                candidate.OccurredAt,
                candidate.SourceTraceParent))
            .SingleOrDefaultAsync(cancellationToken);
        if (webhookEvent is null)
        {
            return null;
        }
        var correlation = new TelemetryCorrelation(
            webhookEvent.ApiClientId,
            webhookEvent.MessageId,
            webhookEvent.DeliveryId,
            webhookEvent.SourceTraceParent);
        using var activity = NotifyRailTelemetry.StartLinkedActivity(
            NotifyRailTelemetry.WebhookReplayActivity,
            ActivityKind.Producer,
            correlation,
            preserveCurrentParent: true);
        activity?.SetTag(
            NotifyRailTelemetry.WebhookEventIdTag,
            webhookEvent.WebhookEventId.ToString());
        activity?.SetTag(
            NotifyRailTelemetry.WebhookEventTypeTag,
            webhookEvent.Type);

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
                    .SetProperty(
                        candidate => candidate.SourceTraceParent,
                        NotifyRailTelemetry.CaptureCurrentTraceParent())
                    .SetProperty(candidate => candidate.UpdatedAt, replayedAt),
                cancellationToken);
        if (updated == 0)
        {
            activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "conflict");
            return null;
        }
        activity?.SetTag(NotifyRailTelemetry.OutcomeTag, "replayed");
        logger.LogInformation(
            "Replayed Webhook Event {notifyrail.webhook_event.id} for Delivery " +
            "{notifyrail.delivery.id}, Message {notifyrail.message.id}, and API Client " +
            "{notifyrail.api_client.id}",
            webhookEvent.WebhookEventId,
            webhookEvent.DeliveryId,
            webhookEvent.MessageId,
            webhookEvent.ApiClientId);

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
        DateTimeOffset OccurredAt,
        string? SourceTraceParent);
}
