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
            .Produces<CreateMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateMessageErrorResponse>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateMessageRequest request,
        NotifyRailDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalization = CreateMessageRequestNormalizer.Normalize(request);
        if (!normalization.IsSuccess)
        {
            return Results.BadRequest(new CreateMessageErrorResponse(normalization.Error!));
        }

        var command = normalization.Command!;
        var createdAt = DateTimeOffset.UtcNow;

        var message = Message.Create(
            command.Type,
            command.Channel,
            command.SenderTitle,
            command.Body,
            command.IdempotencyKey,
            createdAt,
            command.ScheduledAt,
            command.ReportLabel,
            command.Encoding);

        var deliveries = command.Recipients
            .Select(recipient => Delivery.Create(message.Id, recipient, createdAt))
            .ToArray();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Messages.Add(message);
        dbContext.Deliveries.AddRange(deliveries);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = new CreateMessageResponse(
            message.Id,
            deliveries.Length,
            message.CreatedAt);

        return Results.Accepted($"/messages/{message.Id}", response);
    }
}
