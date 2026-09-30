using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PendingHandshakeLifetime = TimeSpan.FromSeconds(5);

    private readonly WifiTransportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly Func<TrustedReconnectServerHandshake>? _handshakeFactory;
    private readonly object _gate = new();
    private readonly Dictionary<uint, long> _pendingHeartbeats = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _runCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;

    private SessionId _sessionId;
    private byte[]? _sessionKey;
    private IPEndPoint? _remoteEndPoint;

    private TrustedReconnectServerHandshake? _pendingHandshake;
    private IPEndPoint? _pendingHandshakeEndPoint;
    private long? _pendingHandshakeStartedAt;

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
        Func<TrustedReconnectServerHandshake>? handshakeFactory = null,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _options.Validate();
        _handshakeFactory = handshakeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _healthEvaluator = new TransportHealthEvaluator();

        if (_options.HasPreAuthenticatedSession)
        {
            _sessionId = _options.SessionId;
            _sessionKey = _options.SessionKey.ToArray();
        }
    }

    public event Action<ProtocolFrame, IPEndPoint>? FrameReceived;

    public event Action<EstablishedTrustedSession, IPEndPoint>? SessionEstablished;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State { get; private set; } = TransportRuntimeState.Available;

    public bool HasAuthenticatedSession
    {
        get
        {
            lock (_gate)
                return _sessionKey is not null && _sessionId != SessionId.Zero;
        }
    }

    public SessionId ActiveSessionId
    {
        get
        {
            lock (_gate)
                return _sessionId;
        }
    }

    public IPEndPoint? RemoteEndPoint
    {
        get
        {
            lock (_gate)
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

        lock (_gate)
        {
            ClearSessionLocked();
            ClearPendingHandshakeLocked();
            _pendingHeartbeats.Clear();
            _previousRtt = null;
            _roundTripTime = TimeSpan.Zero;
            _jitter = TimeSpan.Zero;
            _lastPacketTimestamp = null;
            _lastSequence = null;
            _receivedPackets = 0;
            _lostPackets = 0;
        }

        State = TransportRuntimeState.Available;
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        TimeSpan rtt;
        TimeSpan jitter;
        TimeSpan silence;
        double loss;

        lock (_gate)
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
        UdpClient udp = _udp
            ?? throw new InvalidOperationException("Wi-Fi transport is not connected.");

        byte[] bytes = EncodeWithActiveSession(frame);
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

            if (LooksAuthenticated(result.Buffer))
            {
                await HandleAuthenticatedDatagramAsync(
                    result.Buffer,
                    result.RemoteEndPoint,
                    cancellationToken);
            }
            else
            {
                await HandleHandshakeDatagramAsync(
                    result.Buffer,
                    result.RemoteEndPoint,
                    cancellationToken);
            }
        }
    }

    private async Task HandleHandshakeDatagramAsync(
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        ProtocolFrame frame;

        try
        {
            frame = ProtocolFrameCodec.Decode(datagram);
        }
        catch (Exception exception) when (
            exception is FormatException or
            CryptographicException or
            ArgumentException)
        {
            return;
        }

        if (frame.MessageType == MessageType.Hello)
        {
            await BeginTrustedReconnectAsync(
                frame,
                remoteEndPoint,
                cancellationToken);
            return;
        }

        if (frame.MessageType == MessageType.AuthResponse)
        {
            await CompleteTrustedReconnectAsync(
                frame,
                remoteEndPoint,
                cancellationToken);
        }
    }

    private async Task BeginTrustedReconnectAsync(
        ProtocolFrame helloFrame,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        if (_handshakeFactory is null)
            return;

        TrustedReconnectServerHandshake handshake = _handshakeFactory();
        IReadOnlyList<ProtocolFrame> replies;

        try
        {
            replies = handshake.HandleHello(helloFrame);
        }
        catch (Exception exception) when (
            exception is FormatException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException)
        {
            handshake.Reset();
            return;
        }

        lock (_gate)
        {
            ClearPendingHandshakeLocked();
            _pendingHandshake = handshake;
            _pendingHandshakeEndPoint = remoteEndPoint;
            _pendingHandshakeStartedAt = _timeProvider.GetTimestamp();
        }

        foreach (ProtocolFrame reply in replies)
        {
            await SendEncodedAsync(
                reply,
                remoteEndPoint,
                authenticationKey: null,
                cancellationToken);
        }
    }

    private async Task CompleteTrustedReconnectAsync(
        ProtocolFrame responseFrame,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        TrustedReconnectServerHandshake? handshake;
        long? startedAt;

        lock (_gate)
        {
            handshake = _pendingHandshake;
            startedAt = _pendingHandshakeStartedAt;

            if (_pendingHandshakeEndPoint is null ||
                !_pendingHandshakeEndPoint.Equals(remoteEndPoint))
            {
                return;
            }
        }

        if (handshake is null || startedAt is null)
            return;

        if (_timeProvider.GetElapsedTime(
                startedAt.Value,
                _timeProvider.GetTimestamp()) > PendingHandshakeLifetime)
        {
            lock (_gate)
                ClearPendingHandshakeLocked();

            return;
        }

        ProtocolFrame readyFrame;
        byte[] newSessionKey;
        EstablishedTrustedSession established;

        try
        {
            readyFrame = handshake.HandleAuthResponse(
                responseFrame,
                currentTransport: 2);

            newSessionKey = handshake.GetSessionKey();
            established = handshake.EstablishedSession
                ?? throw new InvalidOperationException(
                    "Handshake authenticated without established session.");
        }
        catch (Exception exception) when (
            exception is FormatException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            CryptographicException)
        {
            lock (_gate)
                ClearPendingHandshakeLocked();

            return;
        }

        await SendEncodedAsync(
            readyFrame,
            remoteEndPoint,
            newSessionKey,
            cancellationToken);

        lock (_gate)
        {
            ActivateSessionLocked(
                established.SessionId,
                newSessionKey,
                remoteEndPoint);

            ClearPendingHandshakeLocked();
        }

        State = TransportRuntimeState.Ready;
        SessionEstablished?.Invoke(established, remoteEndPoint);
    }

    private async Task HandleAuthenticatedDatagramAsync(
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        byte[]? key;
        SessionId sessionId;

        lock (_gate)
        {
            key = _sessionKey?.ToArray();
            sessionId = _sessionId;
        }

        if (key is null || sessionId == SessionId.Zero)
            return;

        ProtocolFrame frame;

        try
        {
            frame = ProtocolFrameCodec.Decode(datagram, key);
        }
        catch (Exception exception) when (
            exception is FormatException or
            CryptographicException or
            ArgumentException)
        {
            CryptographicOperations.ZeroMemory(key);
            return;
        }

        CryptographicOperations.ZeroMemory(key);

        if (frame.SessionId != sessionId)
            return;

        lock (_gate)
        {
            _remoteEndPoint = remoteEndPoint;
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
            await SendHeartbeatAckAsync(
                frame,
                remoteEndPoint,
                cancellationToken);
        }

        FrameReceived?.Invoke(frame, remoteEndPoint);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            IPEndPoint? endpoint;
            SessionId sessionId;

            lock (_gate)
            {
                endpoint = _remoteEndPoint;
                sessionId = _sessionId;
            }

            if (endpoint is null || sessionId == SessionId.Zero)
                continue;

            uint probeId;
            long sentAt = _timeProvider.GetTimestamp();

            lock (_gate)
            {
                probeId = unchecked(++_nextProbeId);
                _pendingHeartbeats[probeId] = sentAt;

                if (_pendingHeartbeats.Count > 32)
                {
                    foreach (uint stale in _pendingHeartbeats.Keys
                                 .Take(_pendingHeartbeats.Count - 32)
                                 .ToArray())
                    {
                        _pendingHeartbeats.Remove(stale);
                    }
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
                    new HeartbeatPayload(probeId)));

            try
            {
                await SendAsync(frame, endpoint, cancellationToken);
            }
            catch (Exception exception) when (
                exception is SocketException or
                InvalidOperationException)
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

        lock (_gate)
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
                    : (_jitter.TotalMilliseconds * 0.75) +
                      (deltaMs * 0.25);

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

        SessionId sessionId;

        lock (_gate)
            sessionId = _sessionId;

        var ack = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.HeartbeatAck,
            FrameFlags.Authenticated,
            sessionId,
            0,
            GetMonotonicMicroseconds(),
            ControlPayloadCodec.EncodeHeartbeat(payload));

        await SendAsync(ack, endpoint, cancellationToken);
    }

    private byte[] EncodeWithActiveSession(ProtocolFrame frame)
    {
        if (!frame.Flags.HasFlag(FrameFlags.Authenticated))
            return ProtocolFrameCodec.Encode(frame);

        byte[]? key;

        lock (_gate)
            key = _sessionKey?.ToArray();

        if (key is null)
        {
            throw new InvalidOperationException(
                "Authenticated Wi-Fi frame requires an active session.");
        }

        try
        {
            return ProtocolFrameCodec.Encode(frame, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task SendEncodedAsync(
        ProtocolFrame frame,
        IPEndPoint endpoint,
        byte[]? authenticationKey,
        CancellationToken cancellationToken)
    {
        UdpClient udp = _udp
            ?? throw new InvalidOperationException(
                "Wi-Fi transport is not connected.");

        byte[] bytes = authenticationKey is null
            ? ProtocolFrameCodec.Encode(frame)
            : ProtocolFrameCodec.Encode(frame, authenticationKey);

        await udp.SendAsync(bytes, endpoint, cancellationToken);
    }

    private void ActivateSessionLocked(
        SessionId sessionId,
        byte[] sessionKey,
        IPEndPoint remoteEndPoint)
    {
        ClearSessionLocked();

        _sessionId = sessionId;
        _sessionKey = sessionKey.ToArray();
        _remoteEndPoint = remoteEndPoint;
        _lastPacketTimestamp = _timeProvider.GetTimestamp();
        _lastSequence = null;
        _receivedPackets = 0;
        _lostPackets = 0;
        _pendingHeartbeats.Clear();
        _previousRtt = null;
        _roundTripTime = TimeSpan.Zero;
        _jitter = TimeSpan.Zero;
    }

    private void ClearSessionLocked()
    {
        if (_sessionKey is not null)
            CryptographicOperations.ZeroMemory(_sessionKey);

        _sessionKey = null;
        _sessionId = SessionId.Zero;
        _remoteEndPoint = null;
    }

    private void ClearPendingHandshakeLocked()
    {
        if (_pendingHandshake is not null)
            _pendingHandshake.Reset();

        _pendingHandshake = null;
        _pendingHandshakeEndPoint = null;
        _pendingHandshakeStartedAt = null;
    }

    private ulong GetMonotonicMicroseconds()
    {
        long timestamp = _timeProvider.GetTimestamp();
        long frequency = _timeProvider.TimestampFrequency;

        if (timestamp <= 0 || frequency <= 0)
            return 0;

        long seconds = timestamp / frequency;
        long remainder = timestamp % frequency;

        return checked(
            (ulong)seconds * 1_000_000UL +
            (ulong)((remainder * 1_000_000L) / frequency));
    }

    private void TrackSequenceLocked(uint sequence)
    {
        if (_lastSequence is uint previous &&
            SequenceNumber.IsNewer(sequence, previous))
        {
            uint distance = sequence - previous;

            if (distance > 1 && distance < int.MaxValue)
                _lostPackets += distance - 1;
        }

        if (_lastSequence is null ||
            SequenceNumber.IsNewer(sequence, _lastSequence.Value))
        {
            _lastSequence = sequence;
        }

        _receivedPackets++;
    }

    private static bool LooksAuthenticated(byte[] datagram)
    {
        return datagram.Length >= ProtocolConstants.HeaderSize &&
               (datagram[7] & (byte)FrameFlags.Authenticated) != 0;
    }
}
