namespace NotifyRail.Api.Features.Messages.CreateMessage;

internal sealed record CreateMessageReadResult(
    bool IsSuccess,
    CreateMessageRequest? Request,
    string? Error)
{
    public static CreateMessageReadResult Success(CreateMessageRequest request)
    {
        return new CreateMessageReadResult(true, request, null);
    }

    public static CreateMessageReadResult Failure(string error)
    {
        return new CreateMessageReadResult(false, null, error);
    }
}
