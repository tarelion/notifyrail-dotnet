using NotifyRail.Api.Features.Deliveries.Queue;

namespace NotifyRail.Api.Features.Deliveries.Providers;

public interface IProviderSender
{
    Task<ProviderResult> SendAsync(
        ProviderRequest request,
        CancellationToken cancellationToken);
}
