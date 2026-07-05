namespace NotifyRail.Api.Features.Messages.GetMessage;

public static class GetMessageEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}", GetAsync)
            .WithName("GetMessage")
            .Produces<GetMessageResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        MessageSummaryReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
