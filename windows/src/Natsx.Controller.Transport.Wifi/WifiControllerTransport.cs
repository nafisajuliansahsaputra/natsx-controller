using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
{
    private readonly WifiTransportOptions _options;
    private readonly TimeProvider _timeProvider;
    private UdpClient? _udp;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private long? _lastPacketTimestamp;
    private uint? _lastSequence;
    private long _receivedPackets;
    private long _lostPackets;

    public WifiControllerTransport(
        WifiTransportOptions options,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action<ProtocolFrame, IPEndPoint>? FrameReceived;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State { get; private set; } = TransportRuntimeState.Available;

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_udp is not null)
            return ValueTask.CompletedTask;

        _udp = new UdpClient(new IPEndPoint(_options.BindAddress, _options.ListenPort));
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = TransportRuntimeState.Ready;
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? receiveCts = _receiveCts;
        Task? receiveTask = _receiveTask;

        _receiveCts = null;
        _receiveTask = null;

        if (receiveCts is not null)
            await receiveCts.CancelAsync();

        _udp?.Dispose();
        _udp = null;

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        receiveCts?.Dispose();
        State = TransportRuntimeState.Available;
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        TimeSpan silence = _lastPacketTimestamp is null
            ? TimeSpan.MaxValue
            : _timeProvider.GetElapsedTime(_lastPacketTimestamp.Value, _timeProvider.GetTimestamp());

        double loss = _receivedPackets + _lostPackets == 0
            ? 0
            : _lostPackets * 100d / (_receivedPackets + _lostPackets);

        TransportHealthGrade grade = silence switch
        {
            var value when value <= TimeSpan.FromMilliseconds(25) => TransportHealthGrade.Good,
            var value when value <= TimeSpan.FromMilliseconds(50) => TransportHealthGrade.Warning,
            var value when value <= TimeSpan.FromMilliseconds(80) => TransportHealthGrade.Degraded,
            var value when value <= TimeSpan.FromMilliseconds(120) => TransportHealthGrade.Critical,
            _ => TransportHealthGrade.Lost,
        };

        int score = grade switch
        {
            TransportHealthGrade.Good => 90,
            TransportHealthGrade.Warning => 72,
            TransportHealthGrade.Degraded => 55,
            TransportHealthGrade.Critical => 35,
            TransportHealthGrade.Lost => 0,
            _ => 90,
        };

        score = Math.Clamp(score - (int)Math.Round(Math.Min(loss, 20)), 0, 100);

        return new TransportHealthSnapshot(
            TransportKind.Wifi,
            State,
            TimeSpan.Zero,
            TimeSpan.Zero,
            loss,
            silence,
            score,
            grade);
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

            TrackSequence(frame.Sequence);
            _lastPacketTimestamp = _timeProvider.GetTimestamp();
            State = TransportRuntimeState.Active;

            FrameReceived?.Invoke(frame, result.RemoteEndPoint);
        }
    }

    private void TrackSequence(uint sequence)
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
