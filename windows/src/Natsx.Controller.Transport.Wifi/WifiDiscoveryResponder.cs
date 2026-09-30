using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiDiscoveryResponder : IAsyncDisposable
{
    public const int DiscoveryPort = 37073;

    private readonly SessionId _windowsDeviceId;
    private readonly string _receiverName;
    private readonly ushort _controllerPort;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public WifiDiscoveryResponder(
        SessionId windowsDeviceId,
        string receiverName,
        ushort controllerPort = 37074)
    {
        _windowsDeviceId = windowsDeviceId;
        _receiverName = receiverName;
        _controllerPort = controllerPort;

        _ = DiscoveryCodec.EncodeResponse(
            new DiscoveryResponse(_windowsDeviceId, _controllerPort, _receiverName));
    }

    public void Start()
    {
        if (_udp is not null)
            return;

        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort))
        {
            EnableBroadcast = true,
        };

        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    public async ValueTask StopAsync()
    {
        if (_udp is null)
            return;

        CancellationTokenSource? cts = _cts;
        Task? loop = _loop;

        _cts = null;
        _loop = null;

        if (cts is not null)
            await cts.CancelAsync();

        _udp.Dispose();
        _udp = null;

        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        UdpClient udp = _udp ?? throw new InvalidOperationException();

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult incoming;

            try
            {
                incoming = await udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                _ = DiscoveryCodec.DecodeRequest(incoming.Buffer);
            }
            catch (FormatException)
            {
                continue;
            }

            byte[] response = DiscoveryCodec.EncodeResponse(
                new DiscoveryResponse(
                    _windowsDeviceId,
                    _controllerPort,
                    _receiverName));

            await udp.SendAsync(response, incoming.RemoteEndPoint, cancellationToken);
        }
    }
}
