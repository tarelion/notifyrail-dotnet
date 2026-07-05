namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public static class MockProviderCallbackEndpoint
{
    public static IEndpointRouteBuilder MapMockProviderCallbackEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/provider-callbacks/mock", ApplyAsync)
            .WithName("ApplyMockProviderCallback")
            .Produces<MockProviderCallbackResponse>(StatusCodes.Status200OK)
            .Produces<MockProviderCallbackErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<MockProviderCallbackErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ApplyAsync(
        MockProviderCallbackRequest request,
        MockProviderCallbackHandler handler,
        CancellationToken cancellationToken)
    {
        var providerMessageId = request.ProviderMessageId?.Trim();
        var status = request.Status?.Trim();
        if (string.IsNullOrWhiteSpace(providerMessageId))
        {
            return Results.BadRequest(
                new MockProviderCallbackErrorResponse(
                    "provider_message_id is required"));
        }
        if (status is not ("delivered" or "failed"))
        {
            return Results.BadRequest(
                new MockProviderCallbackErrorResponse(
                    "status must be one of: delivered, failed"));
        }

        var response = await handler.ApplyAsync(
            providerMessageId,
            status,
            cancellationToken);

        return response is null
            ? Results.NotFound(
                new MockProviderCallbackErrorResponse("provider message not found"))
            : Results.Ok(response);
    }
}
