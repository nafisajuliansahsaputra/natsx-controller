using System.Net;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport :
    IControllerTransport,
    IControllerOutputTransport,
    IControllerStatusOutputTransport,
    IControllerPreferenceSource
{
    private readonly WifiRealtimeReceiver _receiver;
    private readonly IPEndPoint _bindEndPoint;
    private readonly TimeProvider _timeProvider;
    private readonly TransportLifecycle _lifecycle;

    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private bool _disposed;

    public WifiControllerTransport(
        WifiRealtimeReceiver receiver,
        IPEndPoint? bindEndPoint = null,
        TimeProvider? timeProvider = null,
        TransportLifecycle? lifecycle = null)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _receiver.TransportPreferenceReceived +=
            OnTransportPreferenceReceived;
        _bindEndPoint = bindEndPoint ??
            new IPEndPoint(IPAddress.Any, WifiRealtimeReceiver.DefaultPort);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifecycle = lifecycle ?? new TransportLifecycle();
        _lifecycle.StateChanged += OnLifecycleStateChanged;
    }

    public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    public event EventHandler<TransportRuntimeStateChangedEventArgs>? StateChanged;

    public event Action<TransportKind?>?
        PreferredTransportRequested;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State => _lifecycle.State;

    public void SetAuthoritative(bool authoritative)
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

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pumpTask is not null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (State is
            TransportRuntimeState.Unavailable or
            TransportRuntimeState.Available or
            TransportRuntimeState.Failed)
        {
            _lifecycle.SetState(TransportRuntimeState.Connecting);
        }

        var pumpCancellation = new CancellationTokenSource();

        try
        {
            await _receiver.StartAsync(
                _bindEndPoint,
                cancellationToken).ConfigureAwait(false);

            _pumpCancellation = pumpCancellation;
            _pumpTask = PumpStatesAsync(pumpCancellation.Token);

            if (State == TransportRuntimeState.Connecting)
            {
                _lifecycle.SetState(TransportRuntimeState.Stabilizing);
            }
        }
        catch
        {
            pumpCancellation.Dispose();
            _lifecycle.SetState(TransportRuntimeState.Failed);
            throw;
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? pumpCancellation = _pumpCancellation;
        Task? pumpTask = _pumpTask;

        _pumpCancellation = null;
        _pumpTask = null;

        pumpCancellation?.Cancel();

        if (pumpTask is not null)
        {
            try
            {
                await pumpTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _receiver.StopAsync().ConfigureAwait(false);
        pumpCancellation?.Dispose();

        _lifecycle.SetState(TransportRuntimeState.Unavailable);
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        return _receiver.GetHealthSnapshot(State);
    }

    public WifiDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return _receiver.GetDiagnosticsSnapshot(State);
    }

    public ValueTask<bool> TrySendRumbleAsync(
        RumbleState rumble,
        CancellationToken cancellationToken = default)
    {
        return _receiver.TrySendRumbleAsync(
            rumble,
            cancellationToken);
    }

    public ValueTask<bool> TrySendHandoverCommitAsync(
        TransportKind activeTransport,
        uint stateSequence,
        CancellationToken cancellationToken = default)
    {
        ProtocolTransport protocolTransport =
            activeTransport switch
            {
                TransportKind.Wifi =>
                    ProtocolTransport.Wifi,
                TransportKind.Bluetooth =>
                    ProtocolTransport.Bluetooth,
                TransportKind.Usb =>
                    ProtocolTransport.UsbDirect,
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(activeTransport)),
            };

        return _receiver
            .TrySendHandoverCommitAsync(
                protocolTransport,
                stateSequence,
                cancellationToken);
    }

    private async Task PumpStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (WifiGamepadDatagram datagram in
                _receiver.States.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (State == TransportRuntimeState.Stabilizing)
                {
                    _lifecycle.SetState(TransportRuntimeState.Ready);
                }

                GamepadStateReceived?.Invoke(
                    this,
                    new TransportGamepadStateEventArgs(
                        TransportKind.Wifi,
                        datagram.Sequence,
                        datagram.State,
                        _timeProvider.GetTimestamp()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            _lifecycle.SetState(TransportRuntimeState.Failed);
            throw;
        }
    }

    private void OnTransportPreferenceReceived(
        TransportPreferencePayload payload)
    {
        TransportKind? preferred =
            payload.Mode switch
            {
                TransportPreferenceMode.Auto =>
                    null,
                TransportPreferenceMode.Wifi =>
                    TransportKind.Wifi,
                TransportPreferenceMode.Bluetooth =>
                    TransportKind.Bluetooth,
                TransportPreferenceMode.UsbDirect =>
                    TransportKind.Usb,
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(payload)),
            };

        PreferredTransportRequested
            ?.Invoke(preferred);
    }

    private void OnLifecycleStateChanged(TransportRuntimeState state)
    {
        StateChanged?.Invoke(
            this,
            new TransportRuntimeStateChangedEventArgs(
                TransportKind.Wifi,
                state));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await DisconnectAsync(timeout.Token).ConfigureAwait(false);
        await _receiver.DisposeAsync().ConfigureAwait(false);
        _receiver.TransportPreferenceReceived -=
            OnTransportPreferenceReceived;
        _lifecycle.StateChanged -= OnLifecycleStateChanged;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
