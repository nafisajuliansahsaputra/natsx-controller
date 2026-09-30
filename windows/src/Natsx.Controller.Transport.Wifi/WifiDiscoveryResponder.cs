using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

/// <summary>
/// LAN discovery only. A discovery response never authorizes controller input;
/// realtime traffic still requires a trusted authenticated session.
/// </summary>
public sealed class WifiDiscoveryResponder : IAsyncDisposable
{
    public const int DiscoveryPort = 43859;

    private readonly PeerId _receiverPeerId;
    private readonly ushort _realtimePort;
    private readonly TransportCapabilities _capabilities;
    private readonly TimeProvider _timeProvider;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    private long _requestsAccepted;
    private long _requestsRejected;

    public WifiDiscoveryResponder(
        PeerId receiverPeerId,
        ushort realtimePort = WifiRealtimeReceiver.DefaultPort,
        TransportCapabilities capabilities =
            TransportCapabilities.Wifi |
            TransportCapabilities.Bluetooth |
            TransportCapabilities.UsbDirect,
        TimeProvider? timeProvider = null)
    {
        if (!capabilities.HasFlag(TransportCapabilities.Wifi))
        {
            throw new ArgumentException(
                "Wi-Fi discovery responder must advertise Wi-Fi capability.",
                nameof(capabilities));
        }

        if (realtimePort == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(realtimePort));
        }

        _receiverPeerId = receiverPeerId;
        _realtimePort = realtimePort;
        _capabilities = capabilities;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public long RequestsAccepted => Interlocked.Read(ref _requestsAccepted);

    public long RequestsRejected => Interlocked.Read(ref _requestsRejected);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException(
                "Wi-Fi discovery responder is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var client = new UdpClient(
            new IPEndPoint(IPAddress.Any, DiscoveryPort));
        var cancellation = new CancellationTokenSource();

        _udpClient = client;
        _cancellation = cancellation;
        _loop = RunAsync(client, cancellation.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? loop = _loop;
        UdpClient? client = _udpClient;
        CancellationTokenSource? cancellation = _cancellation;

        _loop = null;
        _udpClient = null;
        _cancellation = null;

        if (loop is null)
        {
            return;
        }

        cancellation?.Cancel();
        client?.Dispose();

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(
        UdpClient client,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;

            try
            {
                result = await client.ReceiveAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                ProtocolFrame request = ProtocolFrameCodec.Decode(result.Buffer);

                if (request.MessageType != MessageType.Hello ||
                    request.Flags != FrameFlags.None ||
                    request.SessionId != SessionId.Zero)
                {
                    throw new FormatException("Invalid discovery HELLO envelope.");
                }

                HelloPayload hello = HelloPayloadCodec.Decode(request.Payload);

                if (hello.Role != PeerRole.AndroidController ||
                    !hello.Capabilities.HasFlag(TransportCapabilities.Wifi))
                {
                    throw new FormatException("HELLO is not a Wi-Fi controller discovery request.");
                }

                var responsePayload = new HelloPayload(
                    PeerRole.WindowsReceiver,
                    _capabilities,
                    _receiverPeerId,
                    _realtimePort,
                    hello.DiscoveryNonce);

                byte[] response = ProtocolFrameCodec.Encode(
                    new ProtocolFrame(
                        ProtocolVersion.Current,
                        MessageType.Hello,
                        FrameFlags.None,
                        SessionId.Zero,
                        0,
                        GetMonotonicMicroseconds(),
                        HelloPayloadCodec.Encode(responsePayload)));

                await client.SendAsync(
                    response,
                    result.RemoteEndPoint,
                    cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref _requestsAccepted);
            }
            catch (Exception exception) when (
                exception is FormatException or
                ArgumentException)
            {
                Interlocked.Increment(ref _requestsRejected);
            }
        }
    }

    private ulong GetMonotonicMicroseconds()
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(
            0,
            _timeProvider.GetTimestamp());

        return checked((ulong)(elapsed.Ticks / 10));
    }
}
