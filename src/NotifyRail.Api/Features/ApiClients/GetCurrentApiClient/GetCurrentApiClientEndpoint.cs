using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.GetCurrentApiClient;

public static class GetCurrentApiClientEndpoint
{
    public static IEndpointRouteBuilder MapGetCurrentApiClientEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api-client", ReadAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("GetCurrentApiClient")
            .Produces<GetCurrentApiClientResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        ClaimsPrincipal principal,
        CurrentApiClientReader reader,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var response = await reader.ReadAsync(apiClientId, cancellationToken);
        return response is null ? Results.Unauthorized() : Results.Ok(response);
    }
}
