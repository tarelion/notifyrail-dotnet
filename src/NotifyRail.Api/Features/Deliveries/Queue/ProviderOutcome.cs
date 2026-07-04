namespace NotifyRail.Api.Features.Deliveries.Queue;

public enum ProviderOutcome
{
    Accepted,
    RetryableFailure,
    PermanentFailure,
}
