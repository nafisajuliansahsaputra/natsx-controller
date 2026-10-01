using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Usb;

public sealed class UsbControllerTransport :
    IControllerTransport,
    IControllerOutputTransport
{
    private readonly UsbRealtimeStreamReceiver _receiver;
    private readonly TransportLifecycle _lifecycle;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private bool _disposed;

    public UsbControllerTransport(
        UsbTrustedSession trustedSession,
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
            new UsbRealtimeStreamReceiver(
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
        TransportKind.Usb;

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
                "Usb transport already has an attached realtime stream.");
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
                "Usb transport already has an attached realtime stream.");
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

    public async ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? pumpCancellation =
            _pumpCancellation;
        Task? pumpTask =
            _pumpTask;
         _pumpCancellation = null;
        _pumpTask = null;
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

    public ValueTask<bool> TrySendRumbleAsync(
        RumbleState rumble,
        CancellationToken cancellationToken = default)
    {
        return _receiver.TrySendRumbleAsync(
            rumble,
            cancellationToken);
    }

    private async Task PumpStatesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                UsbGamepadFrame frame in
                _receiver.States.ReadAllAsync(
                    cancellationToken).ConfigureAwait(false))
            {
                GamepadStateReceived?.Invoke(
                    this,
                    new TransportGamepadStateEventArgs(
                        TransportKind.Usb,
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
                TransportKind.Usb,
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
