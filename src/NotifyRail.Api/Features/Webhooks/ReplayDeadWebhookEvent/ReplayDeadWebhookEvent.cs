using NotifyRail.Api.Authentication;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.ReplayDeadWebhookEvent;

public static class ReplayDeadWebhookEvent
{
    public static IEndpointRouteBuilder MapReplayDeadWebhookEvent(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/management/webhook-events/{webhookEventId:guid}/replay",
                ReplayAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("ReplayDeadWebhookEvent")
            .Produces<ReplayDeadWebhookEventResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ReplayAsync(
        Guid webhookEventId,
        WebhookEventReplayer replayer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var replayedAt = PostgresTimestamp.Normalize(timeProvider.GetUtcNow());
        var response = await replayer.ReplayAsync(
            webhookEventId,
            replayedAt,
            cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Accepted(value: response);
    }
}
