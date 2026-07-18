using System.Net;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookEndpointUrlValidator(
    IOptions<WebhookOptions> options,
    WebhookEndpointAddressPolicy addressPolicy)
{
    public async ValueTask<WebhookEndpointUrlValidationResult> ValidateAsync(
        string? value,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return WebhookEndpointUrlValidationResult.Invalid(
                "url must be an absolute HTTP or HTTPS URL");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return WebhookEndpointUrlValidationResult.Invalid(
                "url must not contain user information or a fragment");
        }

        var host = uri.IdnHost.TrimEnd('.');
        var isLocalhost = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            IsLoopbackAddress(host);

        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(isLocalhost && options.Value.AllowLocalhostEndpoints))
        {
            return WebhookEndpointUrlValidationResult.Invalid(
                "url must use HTTPS unless localhost endpoints are explicitly enabled");
        }

        if (isLocalhost && !options.Value.AllowLocalhostEndpoints)
        {
            return WebhookEndpointUrlValidationResult.Invalid(
                "localhost webhook endpoints are not enabled");
        }

        if (!await addressPolicy.IsAllowedAsync(host, cancellationToken))
        {
            return WebhookEndpointUrlValidationResult.Invalid(
                "url host must resolve only to public addresses");
        }

        return WebhookEndpointUrlValidationResult.Valid(uri.AbsoluteUri);
    }

    private static bool IsLoopbackAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        return IPAddress.IsLoopback(
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }
}

public sealed record WebhookEndpointUrlValidationResult(
    bool IsValid,
    string NormalizedUrl,
    string Error)
{
    public static WebhookEndpointUrlValidationResult Valid(string normalizedUrl) =>
        new(true, normalizedUrl, string.Empty);

    public static WebhookEndpointUrlValidationResult Invalid(string error) =>
        new(false, string.Empty, error);
}
