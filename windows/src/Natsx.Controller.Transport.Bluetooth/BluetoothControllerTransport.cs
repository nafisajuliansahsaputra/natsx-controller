using System.Diagnostics;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothControllerTransport :
    IControllerTransport
{
    private static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromMilliseconds(300);

    private readonly BluetoothRfcommConnector _connector;
    private readonly TimeProvider _timeProvider;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly object _gate = new();
    private readonly Dictionary<uint, long> _pendingHeartbeats =
        new();

    private BluetoothTrustedConnection? _connection;
    private CancellationTokenSource? _runCts;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private long? _lastPacketAt;
    private TimeSpan _roundTripTime;
    private TimeSpan _jitter;
    private TimeSpan? _previousRtt;
    private uint _nextProbeId;

    public BluetoothControllerTransport(
        BluetoothRfcommConnector connector,
        TimeProvider? timeProvider = null)
    {
        _connector = connector ??
            throw new ArgumentNullException(nameof(connector));
        _timeProvider =
            timeProvider ?? TimeProvider.System;
        _healthEvaluator =
            new TransportHealthEvaluator();
    }

    public event Action<
        EstablishedTrustedSession>? SessionEstablished;

    public event Action<ProtocolFrame>? FrameReceived;

    public TransportKind Kind =>
        TransportKind.Bluetooth;

    public TransportRuntimeState State { get; private set; } =
        TransportRuntimeState.Available;

    public bool HasAuthenticatedSession
    {
        get
        {
            lock (_gate)
                return _connection is not null;
        }
    }

    public SessionId ActiveSessionId
    {
        get
        {
            lock (_gate)
            {
                return _connection?.Session.SessionId ??
                    SessionId.Zero;
            }
        }
    }

    public async ValueTask ConnectAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_connection is not null ||
                State is
                    TransportRuntimeState.Connecting or
                    TransportRuntimeState.Authenticating)
            {
                return;
            }

            State =
                TransportRuntimeState.Connecting;
        }

        BluetoothTrustedConnection? connection = null;

        try
        {
            connection =
                await _connector.ConnectFirstTrustedAsync(
                    cancellationToken);

            if (connection is null)
            {
                State =
                    TransportRuntimeState.Available;
                return;
            }

            lock (_gate)
            {
                _connection = connection;
                _lastPacketAt =
                    _timeProvider.GetTimestamp();
                State =
                    TransportRuntimeState.Ready;
            }

            _runCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            _receiveLoop =
                ReceiveLoopAsync(_runCts.Token);
            _heartbeatLoop =
                HeartbeatLoopAsync(_runCts.Token);

            SessionEstablished?.Invoke(
                connection.Session);
        }
        catch
        {
            connection?.DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();

            State =
                TransportRuntimeState.Failed;
            throw;
        }
    }

    public async ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = _runCts;
        Task? receiveLoop = _receiveLoop;
        Task? heartbeatLoop = _heartbeatLoop;
        BluetoothTrustedConnection? connection;

        _runCts = null;
        _receiveLoop = null;
        _heartbeatLoop = null;

        lock (_gate)
        {
            connection = _connection;
            _connection = null;
        }

        if (cts is not null)
            await cts.CancelAsync();

        if (connection is not null)
            await connection.DisposeAsync();

        foreach (Task? task in
            new[] { receiveLoop, heartbeatLoop })
        {
            if (task is null)
                continue;

            try
            {
                await task.WaitAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        }

        cts?.Dispose();

        lock (_gate)
        {
            _pendingHeartbeats.Clear();
            _lastPacketAt = null;
            _roundTripTime = TimeSpan.Zero;
            _jitter = TimeSpan.Zero;
            _previousRtt = null;
            State =
                TransportRuntimeState.Available;
        }
    }

    public TransportHealthSnapshot
        GetHealthSnapshot()
    {
        lock (_gate)
        {
            TimeSpan silence =
                _lastPacketAt is null
                    ? TimeSpan.MaxValue
                    : _timeProvider.GetElapsedTime(
                        _lastPacketAt.Value,
                        _timeProvider.GetTimestamp());

            TransportRuntimeState runtimeState =
                State;

            if (_connection is not null)
            {
                runtimeState =
                    silence > TimeSpan.FromMilliseconds(120)
                        ? TransportRuntimeState.Degraded
                        : TransportRuntimeState.Active;
            }

            return _healthEvaluator.Evaluate(
                new TransportMetrics(
                    TransportKind.Bluetooth,
                    runtimeState,
                    _roundTripTime,
                    _jitter,
                    0,
                    silence));
        }
    }

    public async ValueTask SendAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken =
            default)
    {
        BluetoothTrustedConnection connection;

        lock (_gate)
        {
            connection =
                _connection ??
                throw new InvalidOperationException(
                    "Bluetooth transport has no authenticated session.");
        }

        await connection.WriteAsync(
            frame,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(
            CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            BluetoothTrustedConnection? connection;

            lock (_gate)
                connection = _connection;

            if (connection is null)
                break;

            ProtocolFrame frame;

            try
            {
                frame = await connection.ReadAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (
                exception is IOException or
                EndOfStreamException or
                ObjectDisposedException)
            {
                State =
                    TransportRuntimeState.Failed;
                break;
            }

            lock (_gate)
            {
                _lastPacketAt =
                    _timeProvider.GetTimestamp();
                State =
                    TransportRuntimeState.Active;
            }

            if (frame.MessageType ==
                MessageType.HeartbeatAck)
            {
                HandleHeartbeatAck(frame);
            }
            else if (frame.MessageType ==
                MessageType.Heartbeat)
            {
                await AcknowledgeHeartbeatAsync(
                    frame,
                    cancellationToken);
            }

            FrameReceived?.Invoke(frame);
        }
    }

    private async Task HeartbeatLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                HeartbeatInterval,
                _timeProvider);

        while (await timer.WaitForNextTickAsync(
            cancellationToken))
        {
            SessionId sessionId =
                ActiveSessionId;

            if (sessionId == SessionId.Zero)
                continue;

            uint probe;
            long sentAt =
                _timeProvider.GetTimestamp();

            lock (_gate)
            {
                probe = unchecked(++_nextProbeId);
                _pendingHeartbeats[probe] =
                    sentAt;

                while (_pendingHeartbeats.Count > 32)
                {
                    uint oldest =
                        _pendingHeartbeats.Keys.First();
                    _pendingHeartbeats.Remove(oldest);
                }
            }

            var frame = new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                sessionId,
                0,
                GetMonotonicMicroseconds(),
                ControlPayloadCodec.EncodeHeartbeat(
                    new HeartbeatPayload(probe)));

            try
            {
                await SendAsync(
                    frame,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidOperationException or
                ObjectDisposedException)
            {
                State =
                    TransportRuntimeState.Failed;
                break;
            }
        }
    }

    private void HandleHeartbeatAck(
        ProtocolFrame frame)
    {
        HeartbeatPayload payload;

        try
        {
            payload =
                ControlPayloadCodec.DecodeHeartbeat(
                    frame.Payload);
        }
        catch (FormatException)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pendingHeartbeats.Remove(
                    payload.ProbeId,
                    out long sentAt))
            {
                return;
            }

            TimeSpan rtt =
                _timeProvider.GetElapsedTime(
                    sentAt,
                    _timeProvider.GetTimestamp());

            if (_previousRtt is TimeSpan previous)
            {
                double delta =
                    Math.Abs(
                        (rtt - previous)
                        .TotalMilliseconds);

                double next =
                    _jitter == TimeSpan.Zero
                        ? delta
                        : _jitter.TotalMilliseconds *
                            0.75 +
                            delta * 0.25;

                _jitter =
                    TimeSpan.FromMilliseconds(next);
            }

            _previousRtt = rtt;
            _roundTripTime = rtt;
        }
    }

    private async Task AcknowledgeHeartbeatAsync(
        ProtocolFrame request,
        CancellationToken cancellationToken)
    {
        HeartbeatPayload heartbeat;

        try
        {
            heartbeat =
                ControlPayloadCodec.DecodeHeartbeat(
                    request.Payload);
        }
        catch (FormatException)
        {
            return;
        }

        var response = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.HeartbeatAck,
            FrameFlags.Authenticated,
            ActiveSessionId,
            0,
            GetMonotonicMicroseconds(),
            ControlPayloadCodec.EncodeHeartbeat(
                heartbeat));

        await SendAsync(
            response,
            cancellationToken);
    }

    private static ulong GetMonotonicMicroseconds()
    {
        long timestamp = Stopwatch.GetTimestamp();
        long frequency = Stopwatch.Frequency;
        long seconds = timestamp / frequency;
        long remainder = timestamp % frequency;

        return checked(
            (ulong)seconds * 1_000_000UL +
            (ulong)(
                remainder * 1_000_000L /
                frequency));
    }
}
