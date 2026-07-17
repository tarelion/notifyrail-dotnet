using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Messages.GetMessageReport;

public static class GetMessageReportEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageReportEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}/report", GetAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("GetMessageReport")
            .Produces<GetMessageReportResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageReportErrorResponse>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        ClaimsPrincipal principal,
        MessageReportReader reader,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var response = await reader.ReadAsync(apiClientId, messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageReportErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
