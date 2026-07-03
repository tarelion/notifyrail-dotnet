namespace NotifyRail.Api.Features.Messages.CreateMessage;

internal sealed class CreateMessageNormalizationResult
{
    private CreateMessageNormalizationResult(CreateMessageCommand? command, string? error)
    {
        Command = command;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public CreateMessageCommand? Command { get; }

    public string? Error { get; }

    public static CreateMessageNormalizationResult Success(CreateMessageCommand command)
    {
        return new CreateMessageNormalizationResult(command, null);
    }

    public static CreateMessageNormalizationResult Failure(string error)
    {
        return new CreateMessageNormalizationResult(null, error);
    }
}
