using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.CreateApiKey;

public static class CreateApiKeyEndpoint
{
    public static IEndpointRouteBuilder MapCreateApiKeyEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/management/api-clients/{apiClientId:guid}/api-keys", CreateAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("CreateApiKey")
            .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        Guid apiClientId,
        CreateApiKeyRequest request,
        ApiKeyCreator creator,
        CancellationToken cancellationToken)
    {
        var response = await creator.CreateAsync(apiClientId, request.ExpiresAt, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Created(
                $"/management/api-clients/{apiClientId}/api-keys/{response.ApiKeyId}",
                response);
    }
}
