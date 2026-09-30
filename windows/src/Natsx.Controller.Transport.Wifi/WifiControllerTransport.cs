using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HeartbeatExpiry = TimeSpan.FromSeconds(5);

    private readonly WifiTransportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly WifiTransportStatistics _statistics;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, long> _pendingHeartbeats = new();

    private CancellationTokenSource? _connectionLifetime;
    private UdpClient? _udpClient;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private IPEndPoint? _remoteEndpoint;
    private long _heartbeatNonce;
    private int _stateValue = (int)TransportRuntimeState.Unavailable;
    private bool _disposed;

    public WifiControllerTransport(
        WifiTransportOptions options,
        ConnectionPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _statistics = new WifiTransportStatistics(_timeProvider);
        _healthEvaluator = new TransportHealthEvaluator(policy);
    }

    public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State =>
        (TransportRuntimeState)Volatile.Read(ref _stateValue);

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_udpClient is not null)
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SetState(TransportRuntimeState.Connecting);

        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, _options.RealtimePort));

        var lifetime = new CancellationTokenSource();

        _udpClient = client;
        _connectionLifetime = lifetime;
        _receiveTask = ReceiveLoopAsync(client, lifetime.Token);
        _heartbeatTask = HeartbeatLoopAsync(lifetime.Token);

        SetState(TransportRuntimeState.Ready);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? lifetime = _connectionLifetime;
        UdpClient? client = _udpClient;

        if (client is null)
        {
            SetState(TransportRuntimeState.Unavailable);
            return;
        }

        _connectionLifetime = null;
        _udpClient = null;
        _remoteEndpoint = null;

        lifetime?.Cancel();
        client.Dispose();

        Task? receiveTask = _receiveTask;
        Task? heartbeatTask = _heartbeatTask;
        _receiveTask = null;
        _heartbeatTask = null;

        if (receiveTask is not null)
        {
            await IgnoreExpectedShutdownAsync(receiveTask, cancellationToken);
        }

        if (heartbeatTask is not null)
        {
            await IgnoreExpectedShutdownAsync(heartbeatTask, cancellationToken);
        }

        lifetime?.Dispose();
        _pendingHeartbeats.Clear();
        SetState(TransportRuntimeState.Unavailable);
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        return _healthEvaluator.Evaluate(_statistics.Snapshot(State));
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult datagram = await client.ReceiveAsync(cancellationToken);

                ProtocolFrame frame;
                try
                {
                    frame = ProtocolFrameCodec.Decode(
                        datagram.Buffer,
                        _options.AuthenticationKey);
                }
                catch (Exception exception) when (
                    exception is FormatException or
                    System.Security.Cryptography.CryptographicException or
                    ArgumentException)
                {
                    continue;
                }

                if (!frame.Flags.HasFlag(FrameFlags.Authenticated) ||
                    frame.SessionId != _options.SessionId)
                {
                    continue;
                }

                _remoteEndpoint = datagram.RemoteEndPoint;

                switch (frame.MessageType)
                {
                    case MessageType.GamepadState:
                        HandleGamepadState(frame);
                        break;

                    case MessageType.Heartbeat:
                        await SendHeartbeatAckAsync(frame, datagram.RemoteEndPoint, cancellationToken);
                        break;

                    case MessageType.HeartbeatAck:
                        HandleHeartbeatAck(frame);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            SetState(TransportRuntimeState.Failed);
            throw;
        }
    }

    private void HandleGamepadState(ProtocolFrame frame)
    {
        GamepadState state;
        try
        {
            state = GamepadStateCodec.Decode(frame.Payload);
        }
        catch (FormatException)
        {
            return;
        }

        _statistics.RecordState(frame.Sequence);
        SetState(TransportRuntimeState.Active);

        GamepadStateReceived?.Invoke(
            this,
            new TransportGamepadStateEventArgs(
                TransportKind.Wifi,
                frame.Sequence,
                state,
                _timeProvider.GetTimestamp()));
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                PruneExpiredHeartbeats();

                IPEndPoint? endpoint = _remoteEndpoint;
                if (endpoint is null)
                {
                    continue;
                }

                ulong nonce = unchecked((ulong)Interlocked.Increment(ref _heartbeatNonce));
                long sentAt = _timeProvider.GetTimestamp();

                var payload = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(payload, nonce);

                var frame = new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.Heartbeat,
                    FrameFlags.Authenticated,
                    _options.SessionId,
                    0,
                    ToMonotonicMicros(sentAt),
                    payload);

                _pendingHeartbeats[nonce] = sentAt;

                try
                {
                    await SendFrameAsync(frame, endpoint, cancellationToken);
                }
                catch (SocketException)
                {
                    _pendingHeartbeats.TryRemove(nonce, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendHeartbeatAckAsync(
        ProtocolFrame request,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        if (request.Payload.Length != 8)
        {
            return;
        }

        var response = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.HeartbeatAck,
            FrameFlags.Authenticated,
            _options.SessionId,
            0,
            ToMonotonicMicros(_timeProvider.GetTimestamp()),
            request.Payload.ToArray());

        await SendFrameAsync(response, endpoint, cancellationToken);
    }

    private void HandleHeartbeatAck(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return;
        }

        ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload);

        if (!_pendingHeartbeats.TryRemove(nonce, out long sentAt))
        {
            return;
        }

        TimeSpan rtt = _timeProvider.GetElapsedTime(
            sentAt,
            _timeProvider.GetTimestamp());

        _statistics.RecordHeartbeatRoundTrip(rtt);
    }

    private async ValueTask SendFrameAsync(
        ProtocolFrame frame,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        UdpClient? client = _udpClient;
        if (client is null)
        {
            return;
        }

        byte[] bytes = ProtocolFrameCodec.Encode(
            frame,
            _options.AuthenticationKey);

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await client.SendAsync(bytes, endpoint, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void PruneExpiredHeartbeats()
    {
        long now = _timeProvider.GetTimestamp();

        foreach ((ulong nonce, long sentAt) in _pendingHeartbeats)
        {
            if (_timeProvider.GetElapsedTime(sentAt, now) > HeartbeatExpiry)
            {
                _pendingHeartbeats.TryRemove(nonce, out _);
            }
        }
    }

    private ulong ToMonotonicMicros(long timestamp)
    {
        double micros =
            timestamp * 1_000_000d /
            _timeProvider.TimestampFrequency;

        return checked((ulong)Math.Max(0, micros));
    }

    private void SetState(TransportRuntimeState state)
    {
        Volatile.Write(ref _stateValue, (int)state);
    }

    private static async Task IgnoreExpectedShutdownAsync(
        Task task,
        CancellationToken cancellationToken)
    {
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
        catch (SocketException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await DisconnectAsync(timeout.Token);

        _sendGate.Dispose();
    }
}
