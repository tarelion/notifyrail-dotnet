namespace NotifyRail.Api.Features.Messages.CreateMessage;

public sealed class CreateMessageOutcome
{
    private CreateMessageOutcome(
        CreateMessageOutcomeKind kind,
        CreateMessageResponse? response,
        string? error)
    {
        Kind = kind;
        Response = response;
        Error = error;
    }

    public CreateMessageOutcomeKind Kind { get; }

    public CreateMessageResponse? Response { get; }

    public string? Error { get; }

    public static CreateMessageOutcome Accepted(CreateMessageResponse response)
    {
        return new CreateMessageOutcome(
            CreateMessageOutcomeKind.Accepted,
            response,
            null);
    }

    public static CreateMessageOutcome IdempotencyConflict(string error)
    {
        return new CreateMessageOutcome(
            CreateMessageOutcomeKind.IdempotencyConflict,
            null,
            error);
    }
}
