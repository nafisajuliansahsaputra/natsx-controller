using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Wifi;
using Natsx.Controller.Trust.Windows;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private static readonly TimeSpan SafetyTickInterval =
        TimeSpan.FromMilliseconds(25);

    private static readonly TimeSpan HealthTickInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly WindowsTrustServices _trustServices;
    private readonly IVirtualGamepadBackend _virtualBackend;
    private readonly ConnectionPolicy _policy;
    private readonly ControllerSession _controllerSession;
    private readonly InputSafetyEngine _inputSafety;
    private readonly SmartConnectionManager _smartConnection;
    private readonly WifiTrustedControlProcessor _wifiControlProcessor;
    private readonly WifiDiscoveryResponder _wifiControlResponder;
    private readonly SemaphoreSlim _wifiSessionGate = new(1, 1);
    private readonly object _stateGate = new();

    private CancellationTokenSource? _runCancellation;
    private Task? _safetyLoop;
    private Task? _healthLoop;
    private WifiControllerTransport? _wifiTransport;
    private WifiRealtimeReceiver? _wifiReceiver;
    private WifiTrustedSession? _wifiSession;
    private bool _started;
    private bool _disposed;

    private ReceiverRuntimeStatus _status =
        ReceiverRuntimeStatus.Stopped;

    public ReceiverRuntime(
        WindowsTrustServices? trustServices = null,
        IVirtualGamepadBackend? virtualBackend = null,
        ConnectionPolicy? policy = null)
    {
        _trustServices =
            trustServices ?? WindowsTrustServices.CreateDefault();

        _virtualBackend =
            virtualBackend ?? new HidMaestroVirtualGamepadBackend();

        _policy = policy ?? ConnectionPolicy.Competitive;
        _controllerSession = new ControllerSession();
        _inputSafety = new InputSafetyEngine(
            _controllerSession,
            _virtualBackend,
            _policy);
        _smartConnection = new SmartConnectionManager(_policy);

        _wifiControlProcessor = new WifiTrustedControlProcessor(
            _trustServices.LocalPeerId);

        _wifiControlResponder = new WifiDiscoveryResponder(
            _trustServices.LocalPeerId,
            trustedControlProcessor: _wifiControlProcessor,
            trustSecretResolver: ResolveTrustSecret);

        _wifiControlResponder.TrustedSessionEstablished +=
            OnTrustedWifiSessionEstablished;
    }

    public event EventHandler<ReceiverRuntimeStatus>? StatusChanged;

    public ReceiverRuntimeStatus Status
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public PeerId LocalPeerId => _trustServices.LocalPeerId;

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        PublishStatus(
            Status with
            {
                State = ReceiverRuntimeState.Starting,
                Detail = "Starting virtual Xbox controller...",
            });

        try
        {
            await _virtualBackend.StartAsync(cancellationToken)
                .ConfigureAwait(false);

            await _wifiControlResponder.StartAsync(cancellationToken)
                .ConfigureAwait(false);

            var runCancellation = new CancellationTokenSource();
            _runCancellation = runCancellation;
            _safetyLoop = SafetyLoopAsync(runCancellation.Token);
            _healthLoop = HealthLoopAsync(runCancellation.Token);
            _started = true;

            PublishStatus(
                Status with
                {
                    State = ReceiverRuntimeState.WaitingForController,
                    Detail = "Receiver ready. Waiting for a trusted controller.",
                    VirtualControllerReady = _virtualBackend.IsStarted,
                });
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);

            PublishStatus(
                Status with
                {
                    State = ReceiverRuntimeState.Faulted,
                    Detail = "Receiver failed to start.",
                    VirtualControllerReady = _virtualBackend.IsStarted,
                });

            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!_started &&
            _runCancellation is null &&
            !_virtualBackend.IsStarted)
        {
            return;
        }

        PublishStatus(
            Status with
            {
                State = ReceiverRuntimeState.Stopping,
                Detail = "Stopping receiver...",
            });

        await StopCoreAsync().ConfigureAwait(false);

        PublishStatus(ReceiverRuntimeStatus.Stopped);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _wifiControlResponder.TrustedSessionEstablished -=
            OnTrustedWifiSessionEstablished;

        await StopCoreAsync().ConfigureAwait(false);

        _wifiControlProcessor.Dispose();
        await _wifiControlResponder.DisposeAsync()
            .ConfigureAwait(false);
        await _virtualBackend.DisposeAsync()
            .ConfigureAwait(false);

        _wifiSessionGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private byte[]? ResolveTrustSecret(PeerId peerId)
    {
        using TrustedPeerMaterial? material =
            _trustServices.TrustedPeers.Get(peerId);

        return material?.CopyTrustSecret();
    }

    private void OnTrustedWifiSessionEstablished(
        object? sender,
        WifiTrustedSessionEstablishedEventArgs args)
    {
        _ = ActivateWifiSessionAsync(args);
    }

    private async Task ActivateWifiSessionAsync(
        WifiTrustedSessionEstablishedEventArgs args)
    {
        await _wifiSessionGate.WaitAsync().ConfigureAwait(false);

        WifiControllerTransport? oldTransport = null;
        WifiRealtimeReceiver? oldReceiver = null;
        WifiTrustedSession? oldSession = null;
        bool replacementSucceeded = false;

        try
        {
            if (_disposed || !_started)
            {
                args.Session.Dispose();
                return;
            }

            oldTransport = _wifiTransport;
            oldReceiver = _wifiReceiver;
            oldSession = _wifiSession;

            var receiver = new WifiRealtimeReceiver(
                args.Session,
                connectionPolicy: _policy);

            var transport = new WifiControllerTransport(receiver);
            transport.GamepadStateReceived += OnWifiGamepadStateReceived;

            try
            {
                await transport.ConnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                transport.GamepadStateReceived -=
                    OnWifiGamepadStateReceived;
                await transport.DisposeAsync().ConfigureAwait(false);
                await receiver.DisposeAsync().ConfigureAwait(false);
                args.Session.Dispose();
                throw;
            }

            _wifiTransport = transport;
            _wifiReceiver = receiver;
            _wifiSession = args.Session;

            _controllerSession.SetAuthoritativeTransport(
                TransportKind.Wifi);
            replacementSucceeded = true;

            PublishStatus(
                Status with
                {
                    State = ReceiverRuntimeState.Connected,
                    Detail = "Trusted controller connected over Wi-Fi.",
                    RemotePeerId = args.RemotePeerId,
                    ActiveTransport = TransportKind.Wifi,
                    VirtualControllerReady =
                        _virtualBackend.IsStarted,
                });
        }
        catch (Exception exception)
        {
            PublishStatus(
                Status with
                {
                    State = ReceiverRuntimeState.Faulted,
                    Detail =
                        "Wi-Fi controller session failed: " +
                        exception.Message,
                });
        }
        finally
        {
            _wifiSessionGate.Release();
        }

        if (replacementSucceeded)
        {
            if (oldTransport is not null)
            {
                oldTransport.GamepadStateReceived -=
                    OnWifiGamepadStateReceived;

                await SafeDisposeAsync(oldTransport)
                    .ConfigureAwait(false);
            }
            else if (oldReceiver is not null)
            {
                await SafeDisposeAsync(oldReceiver)
                    .ConfigureAwait(false);
            }

            oldSession?.Dispose();
        }
    }

    private void OnWifiGamepadStateReceived(
        object? sender,
        TransportGamepadStateEventArgs args)
    {
        if (args.Transport != TransportKind.Wifi)
        {
            return;
        }

        _inputSafety.TryAccept(
            args.Transport,
            args.Sequence,
            args.State);
    }

    private async Task SafetyLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            SafetyTickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                    cancellationToken)
                .ConfigureAwait(false))
            {
                if (_inputSafety.Evaluate())
                {
                    PublishStatus(
                        Status with
                        {
                            Detail =
                                "Input safety watchdog released stale controls.",
                        });
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HealthLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            HealthTickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                    cancellationToken)
                .ConfigureAwait(false))
            {
                WifiControllerTransport? wifi =
                    _wifiTransport;

                if (wifi is null)
                {
                    continue;
                }

                TransportHealthSnapshot health =
                    wifi.GetHealthSnapshot();

                _smartConnection.Report(health);

                HandoverProposal? proposal =
                    _smartConnection.Evaluate();

                if (proposal is { } selected &&
                    selected.To == TransportKind.Wifi &&
                    selected.From is null)
                {
                    _smartConnection.Commit(selected);
                }

                PublishStatus(
                    Status with
                    {
                        ActiveTransport =
                            _controllerSession.AuthoritativeTransport,
                        ConnectionState =
                            _smartConnection.State,
                        WifiHealth = health,
                    });
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopCoreAsync()
    {
        _started = false;

        CancellationTokenSource? runCancellation =
            _runCancellation;
        Task? safetyLoop = _safetyLoop;
        Task? healthLoop = _healthLoop;

        _runCancellation = null;
        _safetyLoop = null;
        _healthLoop = null;

        runCancellation?.Cancel();

        try
        {
            await _wifiControlResponder.StopAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }

        await _wifiSessionGate.WaitAsync().ConfigureAwait(false);

        try
        {
            WifiControllerTransport? transport =
                Interlocked.Exchange(
                    ref _wifiTransport,
                    null);

            WifiRealtimeReceiver? receiver =
                Interlocked.Exchange(
                    ref _wifiReceiver,
                    null);

            WifiTrustedSession? session =
                Interlocked.Exchange(
                    ref _wifiSession,
                    null);

            _controllerSession.ClearAuthority();

            if (transport is not null)
            {
                transport.GamepadStateReceived -=
                    OnWifiGamepadStateReceived;
                await SafeDisposeAsync(transport)
                    .ConfigureAwait(false);
            }

            if (receiver is not null)
            {
                await SafeDisposeAsync(receiver)
                    .ConfigureAwait(false);
            }

            session?.Dispose();

            if (_virtualBackend.IsStarted)
            {
                try
                {
                    _inputSafety.ForceNeutral();
                }
                catch
                {
                }
            }
        }
        finally
        {
            _wifiSessionGate.Release();
        }

        Task[] loops =
            new[] { safetyLoop, healthLoop }
                .Where(static task => task is not null)
                .Cast<Task>()
                .ToArray();

        if (loops.Length > 0)
        {
            try
            {
                await Task.WhenAll(loops)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        runCancellation?.Dispose();

        if (_virtualBackend.IsStarted)
        {
            try
            {
                await _virtualBackend.StopAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private void PublishStatus(
        ReceiverRuntimeStatus status)
    {
        lock (_stateGate)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private static async Task SafeDisposeAsync(
        IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

public enum ReceiverRuntimeState
{
    Stopped,
    Starting,
    WaitingForController,
    Connected,
    Stopping,
    Faulted,
}

public sealed record ReceiverRuntimeStatus(
    ReceiverRuntimeState State,
    string Detail,
    bool VirtualControllerReady,
    PeerId? RemotePeerId,
    TransportKind? ActiveTransport,
    ConnectionManagerState ConnectionState,
    TransportHealthSnapshot? WifiHealth)
{
    public static ReceiverRuntimeStatus Stopped { get; } =
        new(
            ReceiverRuntimeState.Stopped,
            "Receiver stopped.",
            false,
            null,
            null,
            ConnectionManagerState.Disconnected,
            null);
}
