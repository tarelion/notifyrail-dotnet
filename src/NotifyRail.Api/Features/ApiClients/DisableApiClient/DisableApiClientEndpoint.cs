using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.DisableApiClient;

public static class DisableApiClientEndpoint
{
    public static IEndpointRouteBuilder MapDisableApiClientEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/management/api-clients/{apiClientId:guid}/disable", DisableAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("DisableApiClient")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> DisableAsync(
        Guid apiClientId,
        ApiClientDisabler disabler,
        CancellationToken cancellationToken)
    {
        return await disabler.DisableAsync(apiClientId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
