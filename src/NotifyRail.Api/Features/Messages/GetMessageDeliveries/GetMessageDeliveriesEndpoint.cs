using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Messages.GetMessageDeliveries;

public static class GetMessageDeliveriesEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageDeliveriesEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}/deliveries", GetAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("GetMessageDeliveries")
            .Produces<GetMessageDeliveriesResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageDeliveriesErrorResponse>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        ClaimsPrincipal principal,
        MessageDeliveryReader reader,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var response = await reader.ReadAsync(apiClientId, messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageDeliveriesErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
