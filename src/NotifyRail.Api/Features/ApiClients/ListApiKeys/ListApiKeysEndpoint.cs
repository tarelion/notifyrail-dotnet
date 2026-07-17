using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.ListApiKeys;

public static class ListApiKeysEndpoint
{
    public static IEndpointRouteBuilder MapListApiKeysEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/management/api-clients/{apiClientId:guid}/api-keys", ReadAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("ListApiKeys")
            .Produces<ListApiKeysResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        Guid apiClientId,
        ApiKeyMetadataReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(apiClientId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
}
