namespace NotifyRail.Api.Features.Messages.CreateMessage;

public static class CreateMessageEndpoint
{
    public static IEndpointRouteBuilder MapCreateMessageEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/messages", CreateAsync)
            .WithName("CreateMessage")
            .Produces<CreateMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateMessageRequest request,
        MessageIntake messageIntake,
        CancellationToken cancellationToken)
    {
        var normalization = CreateMessageRequestNormalizer.Normalize(request);
        if (!normalization.IsSuccess)
        {
            return Results.BadRequest(new CreateMessageErrorResponse(normalization.Error!));
        }

        var command = normalization.Command!;
        var result = await messageIntake.CreateAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Results.Conflict(new CreateMessageErrorResponse(result.Conflict!));
        }

        return Results.Accepted(
            $"/messages/{result.Response!.MessageId}",
            result.Response);
    }
}
