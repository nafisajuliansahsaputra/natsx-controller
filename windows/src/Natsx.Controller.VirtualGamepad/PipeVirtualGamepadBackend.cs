using System.IO.Pipes;
using System.Text;
using Natsx.Controller.Core;

namespace Natsx.Controller.VirtualGamepad;

public sealed class PipeVirtualGamepadBackend : IVirtualGamepadBackend
{
    private static readonly TimeSpan InitialConnectTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ReconnectDelay =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan StateRefreshInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly object _gate =
        new();

    private readonly object _writeGate =
        new();

    private CancellationTokenSource? _lifetime;
    private NamedPipeClientStream? _pipe;
    private Task? _connectionLoop;
    private TaskCompletionSource<bool>? _initialReady;
    private GamepadState _latestState =
        GamepadState.Neutral;
    private long _submittedStateCount;
    private long _rumblePacketCount;
    private bool _desiredStarted;
    private bool _connected;

    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    public event Action<RumbleState>? RumbleReceived;

    public HidMaestroVirtualGamepadDiagnostics GetDiagnostics() =>
        new(
            IsStarted,
            HidMaestroVirtualGamepadBackend.ProfileId,
            HidMaestroVirtualGamepadBackend.IdentityKey,
            Interlocked.Read(
                ref _submittedStateCount),
            Interlocked.Read(
                ref _rumblePacketCount));

    public async ValueTask StartAsync(
        CancellationToken cancellationToken = default)
    {
        Task readyTask;

        lock (_gate)
        {
            if (_desiredStarted)
            {
                readyTask =
                    _initialReady?.Task ??
                    Task.CompletedTask;
            }
            else
            {
                _desiredStarted =
                    true;
                _latestState =
                    GamepadState.Neutral;

                _lifetime =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                _initialReady =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);

                readyTask =
                    _initialReady.Task;

                _connectionLoop =
                    ConnectionLoopAsync(
                        _lifetime.Token);
            }
        }

        try
        {
            await readyTask
                .WaitAsync(
                    InitialConnectTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await StopAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask StopAsync(
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime;
        Task? connectionLoop;
        NamedPipeClientStream? pipe;

        lock (_gate)
        {
            if (!_desiredStarted)
            {
                return;
            }

            _desiredStarted =
                false;
            _connected =
                false;

            lifetime =
                _lifetime;
            _lifetime =
                null;

            connectionLoop =
                _connectionLoop;
            _connectionLoop =
                null;

            pipe =
                _pipe;
            _pipe =
                null;

            _initialReady =
                null;
            _latestState =
                GamepadState.Neutral;
        }

        lifetime?.Cancel();

        try
        {
            pipe?.Dispose();
        }
        catch
        {
        }

        if (connectionLoop is not null)
        {
            try
            {
                await connectionLoop
                    .WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        lifetime?.Dispose();
    }

    public void Submit(
        GamepadState state)
    {
        NamedPipeClientStream? pipe;

        lock (_gate)
        {
            _latestState =
                state;

            pipe =
                _connected
                    ? _pipe
                    : null;
        }

        if (pipe is null)
        {
            return;
        }

        if (TryWriteLatestState(
                pipe))
        {
            Interlocked.Increment(
                ref _submittedStateCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync()
            .ConfigureAwait(false);

        GC.SuppressFinalize(
            this);
    }

    private async Task ConnectionLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe =
                new NamedPipeClientStream(
                    ".",
                    GamepadHostProtocol.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous |
                    PipeOptions.WriteThrough);

            try
            {
                using var connectTimeout =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                connectTimeout.CancelAfter(
                    TimeSpan.FromSeconds(3));

                await pipe
                    .ConnectAsync(
                        connectTimeout.Token)
                    .ConfigureAwait(false);

                await SendHelloAsync(
                        pipe,
                        cancellationToken)
                    .ConfigureAwait(false);

                await ExpectReadyAsync(
                        pipe,
                        cancellationToken)
                    .ConfigureAwait(false);

                lock (_gate)
                {
                    if (!_desiredStarted)
                    {
                        return;
                    }

                    _pipe =
                        pipe;
                    _connected =
                        true;
                }

                if (!TryWriteLatestState(
                        pipe))
                {
                    continue;
                }

                _initialReady
                    ?.TrySetResult(
                        true);

                using var connectionLifetime =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                Task refreshTask =
                    StateRefreshLoopAsync(
                        pipe,
                        connectionLifetime.Token);

                try
                {
                    await ReadLoopAsync(
                            pipe,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    connectionLifetime.Cancel();

                    try
                    {
                        await refreshTask
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    TimeoutException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    FormatException)
            {
                _ = exception;
            }
            finally
            {
                MarkDisconnected(
                    pipe);

                pipe.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task
                        .Delay(
                            ReconnectDelay,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static async Task SendHelloAsync(
        Stream pipe,
        CancellationToken cancellationToken)
    {
        byte[] frame =
            new byte[
                GamepadHostProtocol.HeaderSize];

        GamepadHostProtocol.EncodeHeader(
            frame,
            GamepadHostMessageType.Hello,
            0);

        await pipe
            .WriteAsync(
                frame,
                cancellationToken)
            .ConfigureAwait(false);

        await pipe
            .FlushAsync(
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExpectReadyAsync(
        Stream pipe,
        CancellationToken cancellationToken)
    {
        byte[] header =
            new byte[
                GamepadHostProtocol.HeaderSize];

        byte[] payload =
            new byte[
                GamepadHostProtocol.MaximumPayloadSize];

        (
            GamepadHostMessageType messageType,
            ushort payloadLength) =
            await ReadFrameAsync(
                    pipe,
                    header,
                    payload,
                    cancellationToken)
                .ConfigureAwait(false);

        if (messageType ==
            GamepadHostMessageType.Error)
        {
            string message =
                Encoding.UTF8.GetString(
                    payload,
                    0,
                    payloadLength);

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(
                    message)
                    ? "The privileged virtual-gamepad host rejected the Receiver."
                    : message);
        }

        if (messageType !=
                GamepadHostMessageType.Ready ||
            payloadLength != 0)
        {
            throw new FormatException(
                "The privileged virtual-gamepad host returned an invalid READY frame.");
        }
    }

    private async Task StateRefreshLoopAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                StateRefreshInterval);

        while (await timer
            .WaitForNextTickAsync(
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (!TryWriteLatestState(
                    pipe))
            {
                return;
            }
        }
    }

    private async Task ReadLoopAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        byte[] header =
            new byte[
                GamepadHostProtocol.HeaderSize];

        byte[] payload =
            new byte[
                GamepadHostProtocol.MaximumPayloadSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            (
                GamepadHostMessageType messageType,
                ushort payloadLength) =
                await ReadFrameAsync(
                        pipe,
                        header,
                        payload,
                        cancellationToken)
                    .ConfigureAwait(false);

            switch (messageType)
            {
                case GamepadHostMessageType.Rumble:
                    RumbleState rumble =
                        GamepadHostProtocol.DecodeRumble(
                            payload.AsSpan(
                                0,
                                payloadLength));

                    Interlocked.Increment(
                        ref _rumblePacketCount);

                    RumbleReceived?.Invoke(
                        rumble);
                    break;

                case GamepadHostMessageType.Error:
                    throw new InvalidOperationException(
                        Encoding.UTF8.GetString(
                            payload,
                            0,
                            payloadLength));

                default:
                    throw new FormatException(
                        $"Unexpected gamepad-host message {messageType}.");
            }
        }
    }

    private static async Task<(
        GamepadHostMessageType MessageType,
        ushort PayloadLength)> ReadFrameAsync(
        Stream pipe,
        byte[] header,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await pipe
            .ReadExactlyAsync(
                header,
                cancellationToken)
            .ConfigureAwait(false);

        (
            GamepadHostMessageType messageType,
            ushort payloadLength) =
            GamepadHostProtocol.DecodeHeader(
                header);

        if (payloadLength > 0)
        {
            await pipe
                .ReadExactlyAsync(
                    payload.AsMemory(
                        0,
                        payloadLength),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return (
            messageType,
            payloadLength);
    }

    private bool TryWriteLatestState(
        NamedPipeClientStream pipe)
    {
        lock (_writeGate)
        {
            GamepadState latest;

            lock (_gate)
            {
                if (!_connected ||
                    !ReferenceEquals(
                        _pipe,
                        pipe))
                {
                    return false;
                }

                latest =
                    _latestState;
            }

            Span<byte> frame =
                stackalloc byte[
                    GamepadHostProtocol.HeaderSize +
                    GamepadHostProtocol.GamepadStatePayloadSize];

            GamepadHostProtocol.EncodeHeader(
                frame,
                GamepadHostMessageType.GamepadState,
                GamepadHostProtocol.GamepadStatePayloadSize);

            GamepadHostProtocol.EncodeGamepadState(
                latest,
                frame[
                    GamepadHostProtocol.HeaderSize..]);

            try
            {
                pipe.Write(
                    frame);
                pipe.Flush();

                return true;
            }
            catch (IOException)
            {
                MarkDisconnected(
                    pipe);
                return false;
            }
            catch (ObjectDisposedException)
            {
                MarkDisconnected(
                    pipe);
                return false;
            }
            catch (InvalidOperationException)
            {
                MarkDisconnected(
                    pipe);
                return false;
            }
        }
    }

    private void MarkDisconnected(
        NamedPipeClientStream pipe)
    {
        lock (_gate)
        {
            if (ReferenceEquals(
                    _pipe,
                    pipe))
            {
                _pipe =
                    null;
                _connected =
                    false;
            }
        }
    }
}
