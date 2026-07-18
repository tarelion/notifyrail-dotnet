using System.Net;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookEndpointUrlValidator(IOptions<WebhookOptions> options)
{
    public bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "url must be an absolute HTTP or HTTPS URL";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "url must not contain user information or a fragment";
            return false;
        }

        var isLocalhost = uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));

        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(isLocalhost && options.Value.AllowLocalhostEndpoints))
        {
            error = "url must use HTTPS unless localhost endpoints are explicitly enabled";
            return false;
        }

        if (isLocalhost && !options.Value.AllowLocalhostEndpoints)
        {
            error = "localhost webhook endpoints are not enabled";
            return false;
        }

        normalized = uri.AbsoluteUri;
        error = string.Empty;
        return true;
    }
}
