using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.InspectDeadWebhookEvent;

public static class InspectDeadWebhookEvent
{
    public static IEndpointRouteBuilder MapInspectDeadWebhookEvent(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/management/webhook-events/{webhookEventId:guid}",
                InspectAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("InspectDeadWebhookEvent")
            .Produces<InspectDeadWebhookEventResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> InspectAsync(
        Guid webhookEventId,
        DeadWebhookEventReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(webhookEventId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
}
