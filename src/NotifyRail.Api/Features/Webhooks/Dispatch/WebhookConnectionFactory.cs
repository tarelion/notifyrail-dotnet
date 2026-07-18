using System.Net;
using System.Net.Sockets;

namespace NotifyRail.Api.Features.Webhooks.Dispatch;

internal sealed class WebhookConnectionFactory(WebhookEndpointAddressPolicy addressPolicy)
{
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await addressPolicy.ResolveAllowedAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);

        Exception? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                socket.Dispose();
                lastException = exception;
            }
        }

        throw new HttpRequestException(
            "Webhook endpoint connection could not be established.",
            lastException);
    }
}
