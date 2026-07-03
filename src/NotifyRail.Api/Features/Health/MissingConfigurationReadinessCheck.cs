namespace NotifyRail.Api.Features.Health;

public sealed class MissingConfigurationReadinessCheck : IReadinessCheck
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
