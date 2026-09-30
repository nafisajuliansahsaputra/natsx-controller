using System.Net;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
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
        _bindEndPoint = bindEndPoint ??
            new IPEndPoint(IPAddress.Any, WifiRealtimeReceiver.DefaultPort);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifecycle = lifecycle ?? new TransportLifecycle();
        _lifecycle.StateChanged += OnLifecycleStateChanged;
    }

    public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    public event EventHandler<TransportRuntimeStateChangedEventArgs>? StateChanged;

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

            if (State is
                TransportRuntimeState.Connecting or
                TransportRuntimeState.Authenticating)
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

    private async Task PumpStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (WifiGamepadDatagram datagram in
                _receiver.States.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (State is
                    TransportRuntimeState.Connecting or
                    TransportRuntimeState.Authenticating or
                    TransportRuntimeState.Stabilizing)
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
        _lifecycle.StateChanged -= OnLifecycleStateChanged;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
