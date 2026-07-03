namespace NotifyRail.Api.Features.Messages.CreateMessage;

public static class CreateMessageEndpoint
{
    public static IEndpointRouteBuilder MapCreateMessageEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/messages", CreateAsync)
            .WithName("CreateMessage")
            .Produces<CreateMessageResponse>(StatusCodes.Status202Accepted);

        return endpoints;
    }

    private static Task<CreateMessageResponse> CreateAsync(
        CreateMessageRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Message creation will be implemented after the API contract is in place.");
    }
}
