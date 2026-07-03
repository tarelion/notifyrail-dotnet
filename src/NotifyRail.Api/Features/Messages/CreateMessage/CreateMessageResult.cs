namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed class CreateMessageResult
{
    private CreateMessageResult(CreateMessageResponse? response, string? conflict)
    {
        Response = response;
        Conflict = conflict;
    }

    public bool IsSuccess => Response is not null;

    public CreateMessageResponse? Response { get; }

    public string? Conflict { get; }

    public static CreateMessageResult Success(CreateMessageResponse response)
    {
        return new CreateMessageResult(response, null);
    }

    public static CreateMessageResult ConflictResult(string conflict)
    {
        return new CreateMessageResult(null, conflict);
    }
}
