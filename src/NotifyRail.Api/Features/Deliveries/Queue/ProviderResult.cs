namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed record ProviderResult(
    ProviderOutcome Outcome,
    string Provider,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
