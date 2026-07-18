using System.Net;

namespace NotifyRail.Api.Features.Webhooks;

public interface IWebhookDnsResolver
{
    ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemWebhookDnsResolver : IWebhookDnsResolver
{
    public async ValueTask<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
