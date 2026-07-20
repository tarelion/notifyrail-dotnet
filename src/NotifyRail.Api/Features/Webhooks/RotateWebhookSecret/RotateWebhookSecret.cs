using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.RotateWebhookSecret;

public static class RotateWebhookSecret
{
    public static IEndpointRouteBuilder MapRotateWebhookSecret(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/management/api-clients/{apiClientId:guid}/webhook-secret/rotate",
                RotateAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("RotateWebhookSecret")
            .Produces<RotateWebhookSecretResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RotateAsync(
        Guid apiClientId,
        WebhookSecretRotator rotator,
        CancellationToken cancellationToken)
    {
        var response = await rotator.RotateAsync(apiClientId, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Created(
                $"/management/api-clients/{apiClientId}/webhook-endpoint",
                response);
    }
}
