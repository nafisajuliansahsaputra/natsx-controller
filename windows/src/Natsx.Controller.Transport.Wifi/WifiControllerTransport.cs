using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);

    private readonly WifiTransportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly object _healthGate = new();
    private readonly Dictionary<uint, long> _pendingHeartbeats = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _runCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private IPEndPoint? _remoteEndPoint;

    private long? _lastPacketTimestamp;
    private uint? _lastSequence;
    private uint _nextProbeId;
    private long _receivedPackets;
    private long _lostPackets;
    private TimeSpan _roundTripTime;
    private TimeSpan _jitter;
    private TimeSpan? _previousRtt;

    public WifiControllerTransport(
        WifiTransportOptions options,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _healthEvaluator = new TransportHealthEvaluator();
    }

    public event Action<ProtocolFrame, IPEndPoint>? FrameReceived;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State { get; private set; } = TransportRuntimeState.Available;

    public IPEndPoint? RemoteEndPoint
    {
        get
        {
            lock (_healthGate)
                return _remoteEndPoint;
        }
    }

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_udp is not null)
            return ValueTask.CompletedTask;

        _udp = new UdpClient(new IPEndPoint(_options.BindAddress, _options.ListenPort));
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = TransportRuntimeState.Ready;
        _receiveTask = ReceiveLoopAsync(_runCts.Token);
        _heartbeatTask = HeartbeatLoopAsync(_runCts.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? runCts = _runCts;
        Task? receiveTask = _receiveTask;
        Task? heartbeatTask = _heartbeatTask;

        _runCts = null;
        _receiveTask = null;
        _heartbeatTask = null;

        if (runCts is not null)
            await runCts.CancelAsync();

        _udp?.Dispose();
        _udp = null;

        foreach (Task? task in new[] { receiveTask, heartbeatTask })
        {
            if (task is null)
                continue;

            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        runCts?.Dispose();

        lock (_healthGate)
        {
            _remoteEndPoint = null;
            _pendingHeartbeats.Clear();
            _previousRtt = null;
            _roundTripTime = TimeSpan.Zero;
            _jitter = TimeSpan.Zero;
        }

        State = TransportRuntimeState.Available;
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        TimeSpan rtt;
        TimeSpan jitter;
        TimeSpan silence;
        double loss;

        lock (_healthGate)
        {
            long now = _timeProvider.GetTimestamp();

            silence = _lastPacketTimestamp is null
                ? TimeSpan.MaxValue
                : _timeProvider.GetElapsedTime(_lastPacketTimestamp.Value, now);

            rtt = _roundTripTime;
            jitter = _jitter;

            long total = _receivedPackets + _lostPackets;
            loss = total == 0
                ? 0
                : _lostPackets * 100d / total;
        }

        TransportRuntimeState runtimeState =
            silence > TimeSpan.FromMilliseconds(120) && State == TransportRuntimeState.Active
                ? TransportRuntimeState.Degraded
                : State;

        return _healthEvaluator.Evaluate(new TransportMetrics(
            TransportKind.Wifi,
            runtimeState,
            rtt,
            jitter,
            loss,
            silence));
    }

    public async ValueTask SendAsync(
        ProtocolFrame frame,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        UdpClient udp = _udp ?? throw new InvalidOperationException("Wi-Fi transport is not connected.");

        byte[] bytes = ProtocolFrameCodec.Encode(frame, _options.SessionKey);
        await udp.SendAsync(bytes, remoteEndPoint, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        UdpClient udp = _udp ?? throw new InvalidOperationException();

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;

            try
            {
                result = await udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            ProtocolFrame frame;

            try
            {
                frame = ProtocolFrameCodec.Decode(result.Buffer, _options.SessionKey);
            }
            catch (Exception exception) when (
                exception is FormatException or
                System.Security.Cryptography.CryptographicException or
                ArgumentException)
            {
                continue;
            }

            if (frame.SessionId != _options.SessionId)
                continue;

            lock (_healthGate)
            {
                _remoteEndPoint = result.RemoteEndPoint;
                _lastPacketTimestamp = _timeProvider.GetTimestamp();

                if (frame.MessageType == MessageType.GamepadState)
                    TrackSequenceLocked(frame.Sequence);
            }

            State = TransportRuntimeState.Active;

            if (frame.MessageType == MessageType.HeartbeatAck)
            {
                TryHandleHeartbeatAck(frame);
            }
            else if (frame.MessageType == MessageType.Heartbeat)
            {
                await SendHeartbeatAckAsync(frame, result.RemoteEndPoint, cancellationToken);
            }

            FrameReceived?.Invoke(frame, result.RemoteEndPoint);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            IPEndPoint? endpoint;

            lock (_healthGate)
                endpoint = _remoteEndPoint;

            if (endpoint is null)
                continue;

            uint probeId;
            long sentAt = _timeProvider.GetTimestamp();

            lock (_healthGate)
            {
                probeId = unchecked(++_nextProbeId);
                _pendingHeartbeats[probeId] = sentAt;

                if (_pendingHeartbeats.Count > 32)
                {
                    foreach (uint stale in _pendingHeartbeats.Keys.Take(_pendingHeartbeats.Count - 32).ToArray())
                        _pendingHeartbeats.Remove(stale);
                }
            }

            var frame = new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                _options.SessionId,
                0,
                GetMonotonicMicroseconds(),
                ControlPayloadCodec.EncodeHeartbeat(new HeartbeatPayload(probeId)));

            try
            {
                await SendAsync(frame, endpoint, cancellationToken);
            }
            catch (SocketException)
            {
                State = TransportRuntimeState.Degraded;
            }
        }
    }

    private void TryHandleHeartbeatAck(ProtocolFrame frame)
    {
        HeartbeatPayload payload;

        try
        {
            payload = ControlPayloadCodec.DecodeHeartbeat(frame.Payload);
        }
        catch (FormatException)
        {
            return;
        }

        lock (_healthGate)
        {
            if (!_pendingHeartbeats.Remove(payload.ProbeId, out long sentAt))
                return;

            long now = _timeProvider.GetTimestamp();
            TimeSpan rtt = _timeProvider.GetElapsedTime(sentAt, now);

            if (_previousRtt is TimeSpan previous)
            {
                double deltaMs = Math.Abs((rtt - previous).TotalMilliseconds);
                double nextJitterMs = _jitter == TimeSpan.Zero
                    ? deltaMs
                    : (_jitter.TotalMilliseconds * 0.75) + (deltaMs * 0.25);

                _jitter = TimeSpan.FromMilliseconds(nextJitterMs);
            }

            _previousRtt = rtt;
            _roundTripTime = rtt;
        }
    }

    private async Task SendHeartbeatAckAsync(
        ProtocolFrame request,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        HeartbeatPayload payload;

        try
        {
            payload = ControlPayloadCodec.DecodeHeartbeat(request.Payload);
        }
        catch (FormatException)
        {
            return;
        }

        var ack = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.HeartbeatAck,
            FrameFlags.Authenticated,
            _options.SessionId,
            0,
            GetMonotonicMicroseconds(),
            ControlPayloadCodec.EncodeHeartbeat(payload));

        await SendAsync(ack, endpoint, cancellationToken);
    }

    private ulong GetMonotonicMicroseconds()
    {
        long timestamp = _timeProvider.GetTimestamp();
        long frequency = _timeProvider.TimestampFrequency;

        if (timestamp <= 0 || frequency <= 0)
            return 0;

        return checked((ulong)((timestamp * 1_000_000L) / frequency));
    }

    private void TrackSequenceLocked(uint sequence)
    {
        if (_lastSequence is uint previous && SequenceNumber.IsNewer(sequence, previous))
        {
            uint distance = sequence - previous;

            if (distance > 1 && distance < int.MaxValue)
                _lostPackets += distance - 1;
        }

        if (_lastSequence is null || SequenceNumber.IsNewer(sequence, _lastSequence.Value))
            _lastSequence = sequence;

        _receivedPackets++;
    }
}
