using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;

public static class RegisterWebhookEndpoint
{
    public static IEndpointRouteBuilder MapRegisterWebhookEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint",
                RegisterAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("RegisterWebhookEndpoint")
            .Produces<RegisterWebhookEndpointResponse>(StatusCodes.Status201Created)
            .Produces<RegisterWebhookEndpointResponse>(StatusCodes.Status200OK)
            .Produces<RegisterWebhookEndpointErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        Guid apiClientId,
        RegisterWebhookEndpointRequest request,
        WebhookEndpointUrlValidator validator,
        WebhookEndpointRegistrar registrar,
        CancellationToken cancellationToken)
    {
        if (!validator.TryNormalize(request.Url, out var url, out var error))
        {
            return Results.BadRequest(new RegisterWebhookEndpointErrorResponse(error));
        }

        var response = await registrar.RegisterAsync(apiClientId, url, cancellationToken);
        if (response is null)
        {
            return Results.NotFound();
        }

        return response.WebhookSecret is null
            ? Results.Ok(response)
            : Results.Created(
                $"/management/api-clients/{apiClientId}/webhook-endpoint",
                response);
    }
}
