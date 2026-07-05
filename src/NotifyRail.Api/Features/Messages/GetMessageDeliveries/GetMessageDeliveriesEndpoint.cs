namespace NotifyRail.Api.Features.Messages.GetMessageDeliveries;

public static class GetMessageDeliveriesEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageDeliveriesEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}/deliveries", GetAsync)
            .WithName("GetMessageDeliveries")
            .Produces<GetMessageDeliveriesResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageDeliveriesErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        MessageDeliveryReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageDeliveriesErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
