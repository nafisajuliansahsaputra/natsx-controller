using System.Net;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiControllerTransport : IControllerTransport
{
    private readonly WifiRealtimeReceiver _receiver;
    private readonly IPEndPoint _bindEndPoint;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private int _state = (int)TransportRuntimeState.Unavailable;
    private bool _disposed;

    public WifiControllerTransport(
        WifiRealtimeReceiver receiver,
        IPEndPoint? bindEndPoint = null,
        TimeProvider? timeProvider = null)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _bindEndPoint = bindEndPoint ??
            new IPEndPoint(IPAddress.Any, WifiRealtimeReceiver.DefaultPort);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    public TransportKind Kind => TransportKind.Wifi;

    public TransportRuntimeState State =>
        (TransportRuntimeState)Volatile.Read(ref _state);

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pumpTask is not null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SetState(TransportRuntimeState.Connecting);

        var pumpCancellation = new CancellationTokenSource();

        try
        {
            await _receiver.StartAsync(
                _bindEndPoint,
                cancellationToken).ConfigureAwait(false);

            _pumpCancellation = pumpCancellation;
            _pumpTask = PumpStatesAsync(pumpCancellation.Token);
            SetState(TransportRuntimeState.Ready);
        }
        catch
        {
            pumpCancellation.Dispose();
            SetState(TransportRuntimeState.Failed);
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

        SetState(TransportRuntimeState.Unavailable);
    }

    public TransportHealthSnapshot GetHealthSnapshot()
    {
        return _receiver.GetHealthSnapshot(State);
    }

    private async Task PumpStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (WifiGamepadDatagram datagram in
                _receiver.States.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                SetState(TransportRuntimeState.Active);

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
            SetState(TransportRuntimeState.Failed);
            throw;
        }
    }

    private void SetState(TransportRuntimeState state)
    {
        Volatile.Write(ref _state, (int)state);
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

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
