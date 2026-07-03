namespace NotifyRail.Api.Features.Health;

public interface IReadinessCheck
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
