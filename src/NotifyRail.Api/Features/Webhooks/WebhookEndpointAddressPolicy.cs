using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookEndpointAddressPolicy(
    IWebhookDnsResolver dnsResolver,
    IOptions<WebhookOptions> options)
{
    // Conservative public-address policy derived from the IANA IPv4 and IPv6
    // Special-Purpose Address Registries, last verified 2026-07-18.
    private static readonly IPNetwork[] NonPublicIpv4Networks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
    ];

    private static readonly IPNetwork GlobalIpv6UnicastNetwork =
        IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork IetfIpv6ProtocolAssignments =
        IPNetwork.Parse("2001::/23");
    private static readonly IPNetwork[] GloballyReachableIetfIpv6Assignments =
    [
        IPNetwork.Parse("2001:1::1/128"),
        IPNetwork.Parse("2001:1::2/128"),
        IPNetwork.Parse("2001:1::3/128"),
        IPNetwork.Parse("2001:3::/32"),
        IPNetwork.Parse("2001:4:112::/48"),
        IPNetwork.Parse("2001:20::/28"),
        IPNetwork.Parse("2001:30::/28"),
    ];
    private static readonly IPNetwork[] NonPublicIpv6GlobalUnicastNetworks =
    [
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("3fff::/20"),
    ];

    public async ValueTask<bool> IsAllowedAsync(
        string host,
        CancellationToken cancellationToken)
    {
        try
        {
            await ResolveAllowedAddressesAsync(host, cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is UnsafeWebhookEndpointException or SocketException or ArgumentException)
        {
            return false;
        }
    }

    public async ValueTask<IPAddress[]> ResolveAllowedAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var normalizedHost = NormalizeHost(host);
        var isLocalhostTarget = IsLocalhostTarget(normalizedHost);
        IPAddress.TryParse(normalizedHost, out var parsedAddress);
        IPAddress[] addresses;
        if (parsedAddress is not null)
        {
            addresses = [parsedAddress];
        }
        else
        {
            addresses = await dnsResolver.ResolveAsync(normalizedHost, cancellationToken);
        }

        if (addresses.Length == 0)
        {
            throw new UnsafeWebhookEndpointException();
        }

        if (isLocalhostTarget)
        {
            if (options.Value.AllowLocalhostEndpoints && addresses.All(IsLoopback))
            {
                return addresses;
            }

            throw new UnsafeWebhookEndpointException();
        }

        if (addresses.All(IsPublic))
        {
            return addresses;
        }

        throw new UnsafeWebhookEndpointException();
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return !NonPublicIpv4Networks.Any(network => network.Contains(address));
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (!GlobalIpv6UnicastNetwork.Contains(address))
        {
            return false;
        }

        if (IetfIpv6ProtocolAssignments.Contains(address))
        {
            return GloballyReachableIetfIpv6Assignments.Any(
                network => network.Contains(address));
        }

        return !NonPublicIpv6GlobalUnicastNetworks.Any(
            network => network.Contains(address));
    }

    public static bool IsLocalhostTarget(string host)
    {
        var normalizedHost = NormalizeHost(host);
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(normalizedHost, out var address) && IsLoopback(address));
    }

    private static bool IsLoopback(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address) ||
            (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                address.GetAddressBytes()[0] == 127);
    }

    private static string NormalizeHost(string host) => host.TrimEnd('.');
}

internal sealed class UnsafeWebhookEndpointException : Exception;
