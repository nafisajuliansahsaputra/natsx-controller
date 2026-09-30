using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiRealtimeReceiver : IAsyncDisposable
{
    public const int DefaultPort = 43860;
    public const int MaximumDatagramSize = 512;

    private static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromMilliseconds(500);

    private readonly WifiTrustedSession _trustedSession;
    private readonly Channel<WifiGamepadDatagram> _latestState;
    private readonly TimeProvider _timeProvider;
    private readonly object _remoteEndpointLock = new();

    private UdpClient? _udpClient;
    private CancellationTokenSource? _runCancellation;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private IPEndPoint? _remoteEndpoint;

    private long _acceptedDatagrams;
    private long _acceptedControlDatagrams;
    private long _rejectedDatagrams;
    private long _heartbeatsSent;
    private long _lastAcceptedTimestamp;
    private long _lastRoundTripTimeMicros = -1;

    public WifiRealtimeReceiver(
        WifiTrustedSession trustedSession,
        TimeProvider? timeProvider = null)
    {
        _trustedSession = trustedSession
            ?? throw new ArgumentNullException(nameof(trustedSession));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _latestState = Channel.CreateBounded<WifiGamepadDatagram>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
    }

    public bool IsRunning => _receiveLoop is not null;

    public long AcceptedDatagrams => Interlocked.Read(ref _acceptedDatagrams);

    public long AcceptedControlDatagrams =>
        Interlocked.Read(ref _acceptedControlDatagrams);

    public long RejectedDatagrams => Interlocked.Read(ref _rejectedDatagrams);

    public long HeartbeatsSent => Interlocked.Read(ref _heartbeatsSent);

    public TimeSpan Silence
    {
        get
        {
            long acceptedAt = Interlocked.Read(ref _lastAcceptedTimestamp);
            return acceptedAt == 0
                ? TimeSpan.MaxValue
                : _timeProvider.GetElapsedTime(
                    acceptedAt,
                    _timeProvider.GetTimestamp());
        }
    }

    public TimeSpan? RoundTripTime
    {
        get
        {
            long micros = Interlocked.Read(ref _lastRoundTripTimeMicros);
            return micros < 0
                ? null
                : TimeSpan.FromMicroseconds(micros);
        }
    }

    public ChannelReader<WifiGamepadDatagram> States => _latestState.Reader;

    public Task StartAsync(
        IPEndPoint bindEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindEndPoint);

        if (_receiveLoop is not null)
        {
            throw new InvalidOperationException("Wi-Fi receiver is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var udpClient = new UdpClient(bindEndPoint);
        var runCancellation = new CancellationTokenSource();

        _udpClient = udpClient;
        _runCancellation = runCancellation;
        _receiveLoop = ReceiveLoopAsync(udpClient, runCancellation.Token);
        _heartbeatLoop = HeartbeatLoopAsync(udpClient, runCancellation.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? receiveLoop = _receiveLoop;
        Task? heartbeatLoop = _heartbeatLoop;
        CancellationTokenSource? cancellation = _runCancellation;
        UdpClient? udpClient = _udpClient;

        _receiveLoop = null;
        _heartbeatLoop = null;
        _runCancellation = null;
        _udpClient = null;

        lock (_remoteEndpointLock)
        {
            _remoteEndpoint = null;
        }

        if (receiveLoop is null && heartbeatLoop is null)
        {
            return;
        }

        cancellation?.Cancel();
        udpClient?.Dispose();

        var pending = new[] { receiveLoop, heartbeatLoop }
            .Where(static task => task is not null)
            .Cast<Task>()
            .ToArray();

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
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
        _latestState.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(
        UdpClient udpClient,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;

            try
            {
                result = await udpClient.ReceiveAsync(cancellationToken)
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

            if (result.Buffer.Length > MaximumDatagramSize ||
                result.Buffer.Length < ProtocolConstants.HeaderSize)
            {
                Interlocked.Increment(ref _rejectedDatagrams);
                continue;
            }

            try
            {
                MessageType messageType = (MessageType)result.Buffer[6];

                switch (messageType)
                {
                    case MessageType.GamepadState:
                    {
                        WifiGamepadDatagram state =
                            WifiRealtimeDatagramCodec.DecodeGamepadState(
                                result.Buffer,
                                _trustedSession);

                        MarkAuthenticatedRemote(result.RemoteEndPoint);
                        Interlocked.Increment(ref _acceptedDatagrams);
                        Interlocked.Exchange(
                            ref _lastAcceptedTimestamp,
                            _timeProvider.GetTimestamp());

                        _latestState.Writer.TryWrite(state);
                        break;
                    }

                    case MessageType.HeartbeatAck:
                    {
                        ulong echoedProbe =
                            WifiControlDatagramCodec.DecodeHeartbeatAck(
                                result.Buffer,
                                _trustedSession);

                        ulong nowMicros = GetMonotonicMicroseconds();

                        if (echoedProbe <= nowMicros)
                        {
                            ulong elapsed = nowMicros - echoedProbe;
                            if (elapsed <= long.MaxValue)
                            {
                                Interlocked.Exchange(
                                    ref _lastRoundTripTimeMicros,
                                    (long)elapsed);
                            }
                        }

                        MarkAuthenticatedRemote(result.RemoteEndPoint);
                        Interlocked.Increment(ref _acceptedControlDatagrams);
                        break;
                    }

                    default:
                        Interlocked.Increment(ref _rejectedDatagrams);
                        break;
                }
            }
            catch (Exception exception) when (
                exception is FormatException or
                CryptographicException or
                ArgumentException)
            {
                Interlocked.Increment(ref _rejectedDatagrams);
            }
        }
    }

    private async Task HeartbeatLoopAsync(
        UdpClient udpClient,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            IPEndPoint? remote;

            lock (_remoteEndpointLock)
            {
                remote = _remoteEndpoint;
            }

            if (remote is null)
            {
                continue;
            }

            try
            {
                byte[] heartbeat =
                    WifiControlDatagramCodec.EncodeHeartbeat(
                        _trustedSession,
                        GetMonotonicMicroseconds());

                await udpClient.SendAsync(
                    heartbeat,
                    remote,
                    cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref _heartbeatsSent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                // Realtime receive remains authoritative for recovery.
                // A failed probe must not tear down the receiver.
            }
        }
    }

    private void MarkAuthenticatedRemote(IPEndPoint remoteEndPoint)
    {
        lock (_remoteEndpointLock)
        {
            _remoteEndpoint = remoteEndPoint;
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
