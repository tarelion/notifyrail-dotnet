using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.DisableWebhookEndpoint;

public static class DisableWebhookEndpoint
{
    public static IEndpointRouteBuilder MapDisableWebhookEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint/disable",
                DisableAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("DisableWebhookEndpoint")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> DisableAsync(
        Guid apiClientId,
        WebhookEndpointDisabler disabler,
        CancellationToken cancellationToken)
    {
        var apiClientExists = await disabler.DisableAsync(apiClientId, cancellationToken);
        return apiClientExists ? Results.NoContent() : Results.NotFound();
    }
}
