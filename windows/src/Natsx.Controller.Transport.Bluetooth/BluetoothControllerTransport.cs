using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Windows.Networking.Sockets;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothControllerTransport : IControllerTransport
{
    private readonly BluetoothRealtimeStreamReceiver _receiver;
    private readonly TransportLifecycle _lifecycle;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private StreamSocket? _socket;
    private bool _disposed;

    public BluetoothControllerTransport(
        BluetoothTrustedSession trustedSession,
        TransportLifecycle? lifecycle = null,
        TimeProvider? timeProvider = null,
        ConnectionPolicy? connectionPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        _lifecycle =
            lifecycle ??
            new TransportLifecycle();
        _timeProvider =
            timeProvider ??
            TimeProvider.System;

        _receiver =
            new BluetoothRealtimeStreamReceiver(
                trustedSession,
                _lifecycle,
                _timeProvider,
                connectionPolicy);

        _lifecycle.StateChanged +=
            OnLifecycleStateChanged;
    }

    public event EventHandler<TransportGamepadStateEventArgs>?
        GamepadStateReceived;

    public event EventHandler<TransportRuntimeStateChangedEventArgs>?
        StateChanged;

    public TransportKind Kind =>
        TransportKind.Bluetooth;

    public TransportRuntimeState State =>
        _lifecycle.State;

    public ValueTask ConnectAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        if (State is
            TransportRuntimeState.Unavailable or
            TransportRuntimeState.Failed)
        {
            _lifecycle.SetState(
                TransportRuntimeState.Available);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask AttachAuthenticatedStreamAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
        ArgumentNullException.ThrowIfNull(inputStream);

        if (_pumpTask is not null)
        {
            throw new InvalidOperationException(
                "Bluetooth transport already has an attached realtime stream.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        _lifecycle.SetState(
            TransportRuntimeState.Stabilizing);

        var pumpCancellation =
            new CancellationTokenSource();

        try
        {
            await _receiver.StartAsync(
                inputStream,
                cancellationToken).ConfigureAwait(false);

            _pumpCancellation = pumpCancellation;
            _pumpTask =
                PumpStatesAsync(
                    pumpCancellation.Token);
        }
        catch
        {
            pumpCancellation.Dispose();
            _lifecycle.SetState(
                TransportRuntimeState.Failed);
            throw;
        }
    }

    public async ValueTask AttachAuthenticatedStreamAsync(
        Stream inputStream,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);

        if (_pumpTask is not null)
        {
            throw new InvalidOperationException(
                "Bluetooth transport already has an attached realtime stream.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        _lifecycle.SetState(
            TransportRuntimeState.Stabilizing);

        var pumpCancellation =
            new CancellationTokenSource();

        try
        {
            await _receiver.StartAsync(
                inputStream,
                outputStream,
                cancellationToken).ConfigureAwait(false);

            _pumpCancellation = pumpCancellation;
            _pumpTask =
                PumpStatesAsync(
                    pumpCancellation.Token);
        }
        catch
        {
            pumpCancellation.Dispose();
            _lifecycle.SetState(
                TransportRuntimeState.Failed);
            throw;
        }
    }

    public async ValueTask AttachAuthenticatedSocketAsync(
        StreamSocket socket,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
        ArgumentNullException.ThrowIfNull(socket);

        Stream inputStream =
            socket.InputStream.AsStreamForRead();
        Stream outputStream =
            socket.OutputStream.AsStreamForWrite();

        try
        {
            await AttachAuthenticatedStreamAsync(
                inputStream,
                outputStream,
                cancellationToken).ConfigureAwait(false);

            _socket = socket;
        }
        catch
        {
            await inputStream.DisposeAsync()
                .ConfigureAwait(false);
            await outputStream.DisposeAsync()
                .ConfigureAwait(false);
            socket.Dispose();
            throw;
        }
    }

    public async ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? pumpCancellation =
            _pumpCancellation;
        Task? pumpTask =
            _pumpTask;
        StreamSocket? socket =
            _socket;

        _pumpCancellation = null;
        _pumpTask = null;
        _socket = null;

        pumpCancellation?.Cancel();

        await _receiver.StopAsync()
            .ConfigureAwait(false);

        if (pumpTask is not null)
        {
            try
            {
                await pumpTask.WaitAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        socket?.Dispose();
        pumpCancellation?.Dispose();

        _lifecycle.SetState(
            TransportRuntimeState.Unavailable);
    }

    public void SetAuthoritative(
        bool authoritative)
    {
        if (authoritative)
        {
            if (_lifecycle.TryTransition(
                    TransportRuntimeState.Ready,
                    TransportRuntimeState.Active))
            {
                return;
            }

            _lifecycle.TryTransition(
                TransportRuntimeState.Degraded,
                TransportRuntimeState.Active);

            return;
        }

        _lifecycle.TryTransition(
            TransportRuntimeState.Active,
            TransportRuntimeState.Ready);
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        return _receiver.GetHealthSnapshot(
            State);
    }

    private async Task PumpStatesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                BluetoothGamepadFrame frame in
                _receiver.States.ReadAllAsync(
                    cancellationToken).ConfigureAwait(false))
            {
                GamepadStateReceived?.Invoke(
                    this,
                    new TransportGamepadStateEventArgs(
                        TransportKind.Bluetooth,
                        frame.Sequence,
                        frame.State,
                        _timeProvider.GetTimestamp()));

                // READY means Smart Connection can safely select this
                // transport. Publish/cache the fresh full-state first so the
                // runtime never observes READY without a handover snapshot.
                if (State == TransportRuntimeState.Stabilizing)
                {
                    _lifecycle.SetState(
                        TransportRuntimeState.Ready);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnLifecycleStateChanged(
        TransportRuntimeState state)
    {
        StateChanged?.Invoke(
            this,
            new TransportRuntimeStateChangedEventArgs(
                TransportKind.Bluetooth,
                state));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(2));

        await DisconnectAsync(
            timeout.Token).ConfigureAwait(false);

        _lifecycle.StateChanged -=
            OnLifecycleStateChanged;

        await _receiver.DisposeAsync()
            .ConfigureAwait(false);

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
