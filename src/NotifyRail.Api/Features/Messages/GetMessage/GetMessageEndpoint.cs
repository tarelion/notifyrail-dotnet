using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Messages.GetMessage;

public static class GetMessageEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}", GetAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("GetMessage")
            .Produces<GetMessageResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageErrorResponse>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        ClaimsPrincipal principal,
        MessageSummaryReader reader,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var response = await reader.ReadAsync(apiClientId, messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
