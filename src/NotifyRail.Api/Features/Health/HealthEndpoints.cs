namespace NotifyRail.Api.Features.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok")));
        endpoints.MapGet("/readyz", CheckReadinessAsync);

        return endpoints;
    }

    private static async Task<IResult> CheckReadinessAsync(
        IReadinessCheck readinessCheck,
        CancellationToken cancellationToken)
    {
        var isReady = await readinessCheck.IsReadyAsync(cancellationToken);

        return isReady
            ? Results.Ok(new ReadinessResponse("ready"))
            : Results.Json(
                new ReadinessResponse("not_ready"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
