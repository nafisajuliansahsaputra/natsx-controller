using System.Net;
using System.Net.Sockets;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiDiscoveryResponder : IAsyncDisposable
{
    private readonly WifiTransportOptions _options;
    private readonly CancellationTokenSource _lifetime = new();

    private UdpClient? _udpClient;
    private Task? _receiveTask;

    public WifiDiscoveryResponder(WifiTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_udpClient is not null)
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));

        _udpClient = client;
        _receiveTask = ReceiveLoopAsync(client, _lifetime.Token);
        return ValueTask.CompletedTask;
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        byte[] response = WifiDiscoveryProtocol.EncodeResponse(
            new WifiDiscoveryResponse(
                checked((ushort)_options.RealtimePort),
                _options.ReceiverId,
                _options.ReceiverName));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult packet = await client.ReceiveAsync(cancellationToken);

                if (!WifiDiscoveryProtocol.IsRequest(packet.Buffer))
                {
                    continue;
                }

                await client.SendAsync(response, packet.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _udpClient?.Dispose();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
    }
}
