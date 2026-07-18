using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Webhooks.ManageWebhookEndpoint;

public static class ManageWebhookEndpointEndpoints
{
    public static IEndpointRouteBuilder MapManageWebhookEndpointEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint",
                RegisterAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("RegisterWebhookEndpoint")
            .Produces<RegisterWebhookEndpointResponse>(StatusCodes.Status201Created)
            .Produces<RegisterWebhookEndpointResponse>(StatusCodes.Status200OK)
            .Produces<WebhookEndpointErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint",
                InspectAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("InspectWebhookEndpoint")
            .Produces<WebhookEndpointResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/management/api-clients/{apiClientId:guid}/webhook-endpoint/disable",
                DisableAsync)
            .RequireAuthorization(AuthenticationPolicies.Operator)
            .WithName("DisableWebhookEndpoint")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        Guid apiClientId,
        RegisterWebhookEndpointRequest request,
        WebhookEndpointUrlValidator validator,
        WebhookEndpointManager manager,
        CancellationToken cancellationToken)
    {
        if (!validator.TryNormalize(request.Url, out var url, out var error))
        {
            return Results.BadRequest(new WebhookEndpointErrorResponse(error));
        }

        var response = await manager.RegisterAsync(apiClientId, url, cancellationToken);
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

    private static async Task<IResult> InspectAsync(
        Guid apiClientId,
        WebhookEndpointManager manager,
        CancellationToken cancellationToken)
    {
        var response = await manager.InspectAsync(apiClientId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> DisableAsync(
        Guid apiClientId,
        WebhookEndpointManager manager,
        CancellationToken cancellationToken)
    {
        var apiClientExists = await manager.DisableAsync(apiClientId, cancellationToken);
        return apiClientExists ? Results.NoContent() : Results.NotFound();
    }
}
