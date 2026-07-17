using System.Text.Json;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public static class MockProviderCallbackEndpoint
{
    public static IEndpointRouteBuilder MapMockProviderCallbackEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/provider-callbacks/mock", ApplyAsync)
            .WithName("ApplyMockProviderCallback")
            .Produces<MockProviderCallbackResponse>(StatusCodes.Status200OK)
            .Produces<MockProviderCallbackErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<MockProviderCallbackErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<MockProviderCallbackErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ApplyAsync(
        HttpRequest httpRequest,
        IProviderCallbackVerifier verifier,
        MockProviderCallbackHandler handler,
        CancellationToken cancellationToken)
    {
        await using var bodyBuffer = new MemoryStream();
        await httpRequest.Body.CopyToAsync(bodyBuffer, cancellationToken);
        var body = bodyBuffer.ToArray();

        if (!verifier.IsAuthentic(httpRequest.Headers, body))
        {
            return Results.Json(
                new MockProviderCallbackErrorResponse(
                    "invalid provider callback authentication"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        MockProviderCallbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<MockProviderCallbackRequest>(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (request is null)
        {
            return Results.BadRequest();
        }

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
