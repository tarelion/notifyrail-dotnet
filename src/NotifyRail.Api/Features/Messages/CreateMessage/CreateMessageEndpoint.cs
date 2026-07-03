using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

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

    private static async Task<IResult> CreateAsync(
        CreateMessageRequest request,
        NotifyRailDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.UtcNow;

        var message = Message.Create(
            request.Type,
            request.Channel,
            request.SenderTitle,
            request.Body,
            request.IdempotencyKey,
            createdAt,
            request.ScheduledAt,
            request.ReportLabel,
            request.Encoding);

        var deliveries = request.Recipients
            .Select(recipient => Delivery.Create(message.Id, recipient, createdAt))
            .ToArray();

        dbContext.Messages.Add(message);
        dbContext.Deliveries.AddRange(deliveries);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new CreateMessageResponse(
            message.Id,
            deliveries.Length,
            message.CreatedAt);

        return Results.Accepted($"/messages/{message.Id}", response);
    }
}
