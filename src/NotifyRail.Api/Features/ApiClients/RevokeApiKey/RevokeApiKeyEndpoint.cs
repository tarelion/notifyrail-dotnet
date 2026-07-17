using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.RevokeApiKey;

public static class RevokeApiKeyEndpoint
{
    public static IEndpointRouteBuilder MapRevokeApiKeyEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/management/api-clients/{apiClientId:guid}/api-keys/{apiKeyId:guid}/revoke",
                RevokeAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("RevokeApiKey")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RevokeAsync(
        Guid apiClientId,
        Guid apiKeyId,
        ApiKeyRevoker revoker,
        CancellationToken cancellationToken)
    {
        return await revoker.RevokeAsync(apiClientId, apiKeyId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
