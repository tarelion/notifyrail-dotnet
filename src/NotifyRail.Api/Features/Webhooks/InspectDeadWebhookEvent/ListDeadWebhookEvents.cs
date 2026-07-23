using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.InspectDeadWebhookEvent;

public static class ListDeadWebhookEvents
{
    public static IEndpointRouteBuilder MapListDeadWebhookEvents(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/management/webhook-events/dead",
                ListAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("ListDeadWebhookEvents")
            .Produces<ListDeadWebhookEventsResponse>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        DeadWebhookEventReader reader,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await reader.ListAsync(cancellationToken));
    }
}
