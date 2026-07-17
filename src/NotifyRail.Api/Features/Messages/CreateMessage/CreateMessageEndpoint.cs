using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

public static class CreateMessageEndpoint
{
    public static IEndpointRouteBuilder MapCreateMessageEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/messages", CreateAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("CreateMessage")
            .Produces<CreateMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        MessageIntake messageIntake,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var readResult = await CreateMessageRequestReader.ReadAsync(
            httpRequest,
            cancellationToken);
        if (!readResult.IsSuccess)
        {
            return Results.BadRequest(new CreateMessageErrorResponse(readResult.Error!));
        }

        var request = readResult.Request!;
        var normalization = CreateMessageRequestNormalizer.Normalize(request);
        if (!normalization.IsSuccess)
        {
            return Results.BadRequest(new CreateMessageErrorResponse(normalization.Error!));
        }

        var command = normalization.Command!;
        var outcome = await messageIntake.CreateAsync(apiClientId, command, cancellationToken);

        return outcome.Kind switch
        {
            CreateMessageOutcomeKind.Accepted => Results.Accepted(
                $"/messages/{outcome.Response!.MessageId}",
                outcome.Response),
            CreateMessageOutcomeKind.IdempotencyConflict => Results.Conflict(
                new CreateMessageErrorResponse(outcome.Error!)),
            _ => throw new InvalidOperationException(
                $"Unknown create-message outcome: {outcome.Kind}"),
        };
    }
}
