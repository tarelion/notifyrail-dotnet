namespace NotifyRail.Api.Features.Health;

public static class HealthEndpoints
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(2);

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadinessTimeout);

        var isReady = await IsReadyAsync(readinessCheck, timeout.Token, cancellationToken);

        return isReady
            ? Results.Ok(new ReadinessResponse("ready"))
            : Results.Json(
                new ReadinessResponse("unavailable"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<bool> IsReadyAsync(
        IReadinessCheck readinessCheck,
        CancellationToken readinessToken,
        CancellationToken requestToken)
    {
        try
        {
            return await readinessCheck.IsReadyAsync(readinessToken);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
