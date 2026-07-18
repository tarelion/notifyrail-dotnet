using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.InspectWebhookEndpoint;

public static class InspectWebhookEndpoint
{
    public static IEndpointRouteBuilder MapInspectWebhookEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint",
                InspectAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("InspectWebhookEndpoint")
            .Produces<InspectWebhookEndpointResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> InspectAsync(
        Guid apiClientId,
        WebhookEndpointReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(apiClientId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
}
