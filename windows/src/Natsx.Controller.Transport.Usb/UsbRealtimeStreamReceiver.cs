using System.Security.Cryptography;
using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public sealed class UsbRealtimeStreamReceiver : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromMilliseconds(500);
    private readonly UsbTrustedSession _trustedSession;
    private readonly TransportLifecycle _lifecycle;
    private readonly TimeProvider _timeProvider;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly UsbHealthTracker _healthTracker = new();
    private readonly Channel<UsbGamepadFrame> _latestState;
    private readonly object _sequenceGate = new();
    private readonly SemaphoreSlim _outputGate = new(1, 1);

    private CancellationTokenSource? _runCancellation;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private bool _hasSequence;
    private uint _lastSequence;
    private long _lastAcceptedTimestamp;
    private long _acceptedFrames;
    private long _acceptedControlFrames;
    private long _rejectedFrames;
    private long _heartbeatsSent;
    private bool _disposed;

    public UsbRealtimeStreamReceiver(
        UsbTrustedSession trustedSession,
        TransportLifecycle lifecycle,
        TimeProvider? timeProvider = null,
        ConnectionPolicy? connectionPolicy = null)
    {
        _trustedSession =
            trustedSession ??
            throw new ArgumentNullException(nameof(trustedSession));
        _lifecycle =
            lifecycle ??
            throw new ArgumentNullException(nameof(lifecycle));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _healthEvaluator =
            new TransportHealthEvaluator(connectionPolicy);

        _latestState =
            Channel.CreateBounded<UsbGamepadFrame>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });
    }

    public ChannelReader<UsbGamepadFrame> States =>
        _latestState.Reader;

    public bool IsRunning => _receiveLoop is not null;

    public long AcceptedFrames =>
        Interlocked.Read(ref _acceptedFrames);

    public long AcceptedControlFrames =>
        Interlocked.Read(ref _acceptedControlFrames);

    public long RejectedFrames =>
        Interlocked.Read(ref _rejectedFrames);

    public long HeartbeatsSent =>
        Interlocked.Read(ref _heartbeatsSent);

    public TimeSpan? RoundTripTime =>
        _healthTracker.Snapshot().RoundTripTime;

    public TimeSpan Jitter =>
        _healthTracker.Snapshot().Jitter;

    public TimeSpan Silence
    {
        get
        {
            long acceptedAt =
                Interlocked.Read(ref _lastAcceptedTimestamp);

            return acceptedAt == 0
                ? TimeSpan.MaxValue
                : _timeProvider.GetElapsedTime(
                    acceptedAt,
                    _timeProvider.GetTimestamp());
        }
    }

    public async ValueTask<bool> TrySendRumbleAsync(
        RumbleState rumble,
        CancellationToken cancellationToken = default)
    {
        Stream? outputStream =
            _outputStream;

        if (outputStream is null ||
            _receiveLoop is null)
        {
            return false;
        }

        byte[] frame =
            UsbControlFrameCodec
                .EncodeRumble(
                    _trustedSession,
                    new RumblePayload(
                        rumble.LowFrequencyMotor,
                        rumble.HighFrequencyMotor),
                    GetMonotonicMicroseconds());

        byte[] framed =
            UsbStreamFrameCodec
                .Encode(
                    frame);

        await _outputGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await outputStream.WriteAsync(
                    framed,
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            _outputGate.Release();
        }
    }

    public TransportHealthSnapshot GetHealthSnapshot(
        TransportRuntimeState state)
    {
        TransportMetrics metrics =
            _healthTracker.ToTransportMetrics(
                state,
                Silence);

        return _healthEvaluator.Evaluate(metrics);
    }

    public Task StartAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        return StartCoreAsync(
            inputStream,
            outputStream: null,
            cancellationToken);
    }

    public Task StartAsync(
        Stream inputStream,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);

        if (!outputStream.CanWrite)
        {
            throw new ArgumentException(
                "Usb control stream must be writable.",
                nameof(outputStream));
        }

        return StartCoreAsync(
            inputStream,
            outputStream,
            cancellationToken);
    }

    private Task StartCoreAsync(
        Stream inputStream,
        Stream? outputStream,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(inputStream);

        if (!inputStream.CanRead)
        {
            throw new ArgumentException(
                "Usb realtime stream must be readable.",
                nameof(inputStream));
        }

        if (_receiveLoop is not null)
        {
            throw new InvalidOperationException(
                "Usb realtime receiver is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var runCancellation =
            new CancellationTokenSource();

        _inputStream = inputStream;
        _outputStream = outputStream;
        _runCancellation = runCancellation;
        _receiveLoop =
            ReceiveLoopAsync(
                inputStream,
                runCancellation.Token);

        if (outputStream is not null)
        {
            _heartbeatLoop =
                HeartbeatLoopAsync(
                    outputStream,
                    runCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation =
            _runCancellation;
        Task? receiveLoop = _receiveLoop;
        Task? heartbeatLoop = _heartbeatLoop;
        Stream? inputStream = _inputStream;
        Stream? outputStream = _outputStream;

        _runCancellation = null;
        _receiveLoop = null;
        _heartbeatLoop = null;
        _inputStream = null;
        _outputStream = null;

        cancellation?.Cancel();

        if (inputStream is not null)
        {
            DisposeStream(inputStream);
        }

        if (outputStream is not null &&
            !ReferenceEquals(
                inputStream,
                outputStream))
        {
            DisposeStream(outputStream);
        }

        var pending =
            new[] { receiveLoop, heartbeatLoop }
                .Where(static task => task is not null)
                .Cast<Task>()
                .ToArray();

        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cancellation?.Dispose();
    }

    private static void DisposeStream(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (NotImplementedException)
        {
            // WinRT USB output streams may not implement FlushAsync.
            // The owning UsbDevice tears down the native pipe lifetime.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync(
        Stream inputStream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] frameBytes =
                    await UsbStreamFrameCodec
                        .ReadFrameAsync(
                            inputStream,
                            cancellationToken)
                        .ConfigureAwait(false);

                MessageType messageType =
                    frameBytes.Length >
                        ProtocolConstants.HeaderSize
                        ? (MessageType)frameBytes[6]
                        : 0;

                if (messageType ==
                    MessageType.HeartbeatAck)
                {
                    try
                    {
                        ulong echoedProbe =
                            UsbControlFrameCodec
                                .DecodeHeartbeatAck(
                                    frameBytes,
                                    _trustedSession);

                        ulong nowMicros =
                            GetMonotonicMicroseconds();

                        if (echoedProbe <= nowMicros)
                        {
                            ulong elapsed =
                                nowMicros - echoedProbe;

                            if (elapsed <= long.MaxValue)
                            {
                                _healthTracker
                                    .RecordRoundTripTime(
                                        TimeSpan
                                            .FromMicroseconds(
                                                (long)elapsed));
                            }
                        }

                        Interlocked.Increment(
                            ref _acceptedControlFrames);
                    }
                    catch (Exception exception) when (
                        exception is FormatException or
                        CryptographicException or
                        ArgumentException)
                    {
                        Interlocked.Increment(
                            ref _rejectedFrames);
                    }

                    continue;
                }

                if (messageType !=
                    MessageType.GamepadState)
                {
                    Interlocked.Increment(
                        ref _rejectedFrames);
                    continue;
                }

                UsbGamepadFrame state;

                try
                {
                    state =
                        UsbRealtimeFrameCodec
                            .DecodeGamepadState(
                                frameBytes,
                                _trustedSession);
                }
                catch (Exception exception) when (
                    exception is FormatException or
                    CryptographicException or
                    ArgumentException)
                {
                    Interlocked.Increment(
                        ref _rejectedFrames);
                    continue;
                }

                lock (_sequenceGate)
                {
                    if (_hasSequence &&
                        !SequenceNumber.IsNewer(
                            state.Sequence,
                            _lastSequence))
                    {
                        Interlocked.Increment(
                            ref _rejectedFrames);
                        continue;
                    }

                    _hasSequence = true;
                    _lastSequence = state.Sequence;
                }

                Interlocked.Exchange(
                    ref _lastAcceptedTimestamp,
                    _timeProvider.GetTimestamp());

                Interlocked.Increment(
                    ref _acceptedFrames);

                _latestState.Writer.TryWrite(state);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (FormatException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(
                    ref _rejectedFrames);

                _lifecycle.SetState(
                    TransportRuntimeState.Failed);
            }
        }
        catch (EndOfStreamException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _lifecycle.SetState(
                    TransportRuntimeState.Failed);
            }
        }
        catch (IOException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _lifecycle.SetState(
                    TransportRuntimeState.Failed);
            }
        }
    }

    private async Task HeartbeatLoopAsync(
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                HeartbeatInterval,
                _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(
                cancellationToken).ConfigureAwait(false))
            {
                byte[] heartbeat =
                    UsbControlFrameCodec
                        .EncodeHeartbeat(
                            _trustedSession,
                            GetMonotonicMicroseconds());

                byte[] framed =
                    UsbStreamFrameCodec
                        .Encode(heartbeat);

                await _outputGate.WaitAsync(
                    cancellationToken).ConfigureAwait(false);

                try
                {
                    await outputStream.WriteAsync(
                        framed,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _outputGate.Release();
                }

                Interlocked.Increment(
                    ref _heartbeatsSent);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _lifecycle.SetState(
                    TransportRuntimeState.Failed);
            }
        }
    }

    private ulong GetMonotonicMicroseconds()
    {
        TimeSpan elapsed =
            _timeProvider.GetElapsedTime(
                0,
                _timeProvider.GetTimestamp());

        return checked(
            (ulong)(elapsed.Ticks / 10));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _latestState.Writer.TryComplete();
        _outputGate.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
