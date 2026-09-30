using System.Security.Cryptography;
using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothRealtimeStreamReceiver : IAsyncDisposable
{
    private readonly BluetoothTrustedSession _trustedSession;
    private readonly TransportLifecycle _lifecycle;
    private readonly TimeProvider _timeProvider;
    private readonly TransportHealthEvaluator _healthEvaluator;
    private readonly Channel<BluetoothGamepadFrame> _latestState;
    private readonly object _sequenceGate = new();

    private CancellationTokenSource? _runCancellation;
    private Task? _receiveLoop;
    private Stream? _inputStream;
    private bool _hasSequence;
    private uint _lastSequence;
    private long _lastAcceptedTimestamp;
    private long _acceptedFrames;
    private long _rejectedFrames;
    private bool _disposed;

    public BluetoothRealtimeStreamReceiver(
        BluetoothTrustedSession trustedSession,
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
            Channel.CreateBounded<BluetoothGamepadFrame>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });
    }

    public ChannelReader<BluetoothGamepadFrame> States =>
        _latestState.Reader;

    public bool IsRunning => _receiveLoop is not null;

    public long AcceptedFrames =>
        Interlocked.Read(ref _acceptedFrames);

    public long RejectedFrames =>
        Interlocked.Read(ref _rejectedFrames);

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

    public TransportHealthSnapshot GetHealthSnapshot(
        TransportRuntimeState state)
    {
        var metrics = new TransportMetrics(
            TransportKind.Bluetooth,
            state,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            Silence);

        return _healthEvaluator.Evaluate(metrics);
    }

    public Task StartAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(inputStream);

        if (!inputStream.CanRead)
        {
            throw new ArgumentException(
                "Bluetooth realtime stream must be readable.",
                nameof(inputStream));
        }

        if (_receiveLoop is not null)
        {
            throw new InvalidOperationException(
                "Bluetooth realtime receiver is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var runCancellation =
            new CancellationTokenSource();

        _inputStream = inputStream;
        _runCancellation = runCancellation;
        _receiveLoop =
            ReceiveLoopAsync(
                inputStream,
                runCancellation.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation =
            _runCancellation;
        Task? receiveLoop = _receiveLoop;
        Stream? inputStream = _inputStream;

        _runCancellation = null;
        _receiveLoop = null;
        _inputStream = null;

        cancellation?.Cancel();

        if (inputStream is not null)
        {
            await inputStream.DisposeAsync()
                .ConfigureAwait(false);
        }

        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop.ConfigureAwait(false);
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

    private async Task ReceiveLoopAsync(
        Stream inputStream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] frameBytes =
                    await BluetoothStreamFrameCodec
                        .ReadFrameAsync(
                            inputStream,
                            cancellationToken)
                        .ConfigureAwait(false);

                BluetoothGamepadFrame state;

                try
                {
                    state =
                        BluetoothRealtimeFrameCodec
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

                if (_lifecycle.State ==
                    TransportRuntimeState.Stabilizing)
                {
                    _lifecycle.SetState(
                        TransportRuntimeState.Ready);
                }

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _latestState.Writer.TryComplete();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
