namespace NotifyRail.Api.Features.Messages.CreateMessage;

public enum CreateMessageOutcomeKind
{
    Accepted,
    IdempotencyConflict,
}
