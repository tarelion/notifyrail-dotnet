using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public static class CreateApiClientEndpoint
{
    public static IEndpointRouteBuilder MapCreateApiClientEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/management/api-clients", CreateAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("CreateApiClient")
            .Produces<CreateApiClientResponse>(StatusCodes.Status201Created)
            .Produces<CreateApiClientErrorResponse>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateApiClientRequest request,
        ApiClientCreator creator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new CreateApiClientErrorResponse("name must not be blank"));
        }

        var response = await creator.CreateAsync(request.Name, cancellationToken);

        return Results.Created(
            $"/management/api-clients/{response.ApiClientId}",
            response);
    }
}
