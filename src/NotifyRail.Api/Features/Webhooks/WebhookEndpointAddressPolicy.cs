using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookEndpointAddressPolicy(
    IWebhookDnsResolver dnsResolver,
    IOptions<WebhookOptions> options)
{
    public async ValueTask<bool> IsAllowedAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var normalizedHost = host.TrimEnd('.');
        var isLocalhostName = string.Equals(
            normalizedHost,
            "localhost",
            StringComparison.OrdinalIgnoreCase);
        var isLoopbackLiteral = IPAddress.TryParse(normalizedHost, out var parsedAddress) &&
            IsLoopback(parsedAddress);
        IPAddress[] addresses;
        if (parsedAddress is not null)
        {
            addresses = [parsedAddress];
        }
        else
        {
            try
            {
                addresses = await dnsResolver.ResolveAsync(normalizedHost, cancellationToken);
            }
            catch (Exception exception) when (
                exception is SocketException or ArgumentException)
            {
                return false;
            }
        }

        if (addresses.Length == 0)
        {
            return false;
        }

        if (isLocalhostName || isLoopbackLiteral)
        {
            return options.Value.AllowLocalhostEndpoints && addresses.All(IsLoopback);
        }

        return addresses.All(IsPublic);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return !IsInIpv4Range(bytes, 0, 8) &&
                !IsInIpv4Range(bytes, 10, 8) &&
                !IsInIpv4Range(bytes, 100, 10, secondOctet: 64) &&
                !IsInIpv4Range(bytes, 127, 8) &&
                !IsInIpv4Range(bytes, 169, 16, secondOctet: 254) &&
                !IsInIpv4Range(bytes, 172, 12, secondOctet: 16) &&
                !IsInIpv4Range(bytes, 192, 24, secondOctet: 0, thirdOctet: 0) &&
                !IsInIpv4Range(bytes, 192, 24, secondOctet: 0, thirdOctet: 2) &&
                !IsInIpv4Range(bytes, 192, 16, secondOctet: 168) &&
                !IsInIpv4Range(bytes, 198, 15, secondOctet: 18) &&
                !IsInIpv4Range(bytes, 198, 24, secondOctet: 51, thirdOctet: 100) &&
                !IsInIpv4Range(bytes, 203, 24, secondOctet: 0, thirdOctet: 113) &&
                !IsInIpv4Range(bytes, 224, 4) &&
                !IsInIpv4Range(bytes, 240, 4);
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return !address.Equals(IPAddress.IPv6Any) &&
            !IPAddress.IsLoopback(address) &&
            !address.IsIPv6LinkLocal &&
            !address.IsIPv6Multicast &&
            !address.IsIPv6SiteLocal &&
            !address.IsIPv6UniqueLocal &&
            !IsInIpv6Range(address, [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 64) &&
            !IsInIpv6Range(address, [0x20, 0x01, 0x0d, 0xb8], 32);
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

    private static bool IsInIpv4Range(
        byte[] address,
        byte firstOctet,
        int prefixLength,
        byte secondOctet = 0,
        byte thirdOctet = 0)
    {
        var value = ((uint)address[0] << 24) |
            ((uint)address[1] << 16) |
            ((uint)address[2] << 8) |
            address[3];
        var network = ((uint)firstOctet << 24) |
            ((uint)secondOctet << 16) |
            ((uint)thirdOctet << 8);
        var mask = uint.MaxValue << (32 - prefixLength);
        return (value & mask) == (network & mask);
    }

    private static bool IsInIpv6Range(
        IPAddress address,
        ReadOnlySpan<byte> network,
        int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (!bytes.AsSpan(0, wholeBytes).SequenceEqual(network[..wholeBytes]))
        {
            return false;
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (bytes[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }
}
