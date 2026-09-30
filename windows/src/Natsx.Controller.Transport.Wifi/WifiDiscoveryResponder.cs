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
    private readonly WifiTrustedControlProcessor? _trustedControlProcessor;
    private readonly Func<PeerId, byte[]?>? _trustSecretResolver;

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
        TimeProvider? timeProvider = null,
        WifiTrustedControlProcessor? trustedControlProcessor = null,
        Func<PeerId, byte[]?>? trustSecretResolver = null)
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
        if ((trustedControlProcessor is null) != (trustSecretResolver is null))
        {
            throw new ArgumentException(
                "Trusted control processor and trust resolver must be configured together.");
        }

        _capabilities = capabilities;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trustedControlProcessor = trustedControlProcessor;
        _trustSecretResolver = trustSecretResolver;
    }

    public event EventHandler<WifiTrustedSessionEstablishedEventArgs>?
        TrustedSessionEstablished;

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
                if (result.Buffer.Length < ProtocolConstants.HeaderSize)
                {
                    throw new FormatException("Control datagram is shorter than the protocol header.");
                }

                MessageType messageType = (MessageType)result.Buffer[6];

                switch (messageType)
                {
                    case MessageType.Hello:
                        await HandleHelloAsync(
                            client,
                            result,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    case MessageType.AuthChallenge:
                        await HandleAuthChallengeAsync(
                            client,
                            result,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    case MessageType.SessionReady:
                        await HandleSessionReadyAsync(
                            client,
                            result,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        throw new FormatException(
                            "Unsupported Wi-Fi control message.");
                }

                Interlocked.Increment(ref _requestsAccepted);
            }
            catch (Exception exception) when (
                exception is FormatException or
                ArgumentException or
                System.Security.Cryptography.CryptographicException)
            {
                Interlocked.Increment(ref _requestsRejected);
            }
        }
    }

    private async Task HandleHelloAsync(
        UdpClient client,
        UdpReceiveResult result,
        CancellationToken cancellationToken)
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
            throw new FormatException(
                "HELLO is not a Wi-Fi controller discovery request.");
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
    }

    private async Task HandleAuthChallengeAsync(
        UdpClient client,
        UdpReceiveResult result,
        CancellationToken cancellationToken)
    {
        WifiTrustedControlProcessor processor =
            _trustedControlProcessor ??
            throw new FormatException(
                "Trusted reconnect is not configured.");

        Func<PeerId, byte[]?> trustResolver =
            _trustSecretResolver ??
            throw new FormatException(
                "Trusted reconnect resolver is not configured.");

        byte[] response = processor.HandleChallenge(
            result.Buffer,
            result.RemoteEndPoint,
            trustResolver,
            GetMonotonicMicroseconds());

        await client.SendAsync(
            response,
            result.RemoteEndPoint,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSessionReadyAsync(
        UdpClient client,
        UdpReceiveResult result,
        CancellationToken cancellationToken)
    {
        WifiTrustedControlProcessor processor =
            _trustedControlProcessor ??
            throw new FormatException(
                "Trusted reconnect is not configured.");

        WifiTrustedControlCompletion completion =
            processor.HandleSessionReady(
                result.Buffer,
                result.RemoteEndPoint,
                GetMonotonicMicroseconds());

        bool transferred = false;

        try
        {
            await client.SendAsync(
                completion.ResponseDatagram,
                result.RemoteEndPoint,
                cancellationToken).ConfigureAwait(false);

            EventHandler<WifiTrustedSessionEstablishedEventArgs>? handler =
                TrustedSessionEstablished;

            if (handler is null)
            {
                return;
            }

            handler(
                this,
                new WifiTrustedSessionEstablishedEventArgs(
                    completion.RemotePeerId,
                    completion.Session,
                    result.RemoteEndPoint));

            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                completion.Dispose();
            }
            else
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                    completion.ResponseDatagram);
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


public sealed class WifiTrustedSessionEstablishedEventArgs : EventArgs
{
    public WifiTrustedSessionEstablishedEventArgs(
        PeerId remotePeerId,
        WifiTrustedSession session,
        IPEndPoint controlEndPoint)
    {
        RemotePeerId = remotePeerId;
        Session = session ?? throw new ArgumentNullException(nameof(session));
        ControlEndPoint = controlEndPoint ??
            throw new ArgumentNullException(nameof(controlEndPoint));
    }

    public PeerId RemotePeerId { get; }

    /// <summary>
    /// Ownership transfers to the event subscriber. The subscriber must
    /// eventually dispose this session.
    /// </summary>
    public WifiTrustedSession Session { get; }

    public IPEndPoint ControlEndPoint { get; }
}
