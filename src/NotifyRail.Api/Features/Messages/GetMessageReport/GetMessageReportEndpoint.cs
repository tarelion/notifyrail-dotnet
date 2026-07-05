namespace NotifyRail.Api.Features.Messages.GetMessageReport;

public static class GetMessageReportEndpoint
{
    public static IEndpointRouteBuilder MapGetMessageReportEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/messages/{messageId:guid}/report", GetAsync)
            .WithName("GetMessageReport")
            .Produces<GetMessageReportResponse>(StatusCodes.Status200OK)
            .Produces<GetMessageReportErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid messageId,
        MessageReportReader reader,
        CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(messageId, cancellationToken);

        return response is null
            ? Results.NotFound(new GetMessageReportErrorResponse("message not found"))
            : Results.Ok(response);
    }
}
