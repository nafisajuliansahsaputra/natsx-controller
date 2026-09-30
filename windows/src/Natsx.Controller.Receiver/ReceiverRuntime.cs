using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Bluetooth;
using Natsx.Controller.Transport.Wifi;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public sealed record ReceiverRuntimeStatus(
    string Message,
    bool VirtualControllerReady,
    bool WifiListening,
    bool SessionActive,
    TransportHealthSnapshot? WifiHealth = null,
    bool BluetoothReady = false,
    TransportHealthSnapshot? BluetoothHealth = null,
    TransportKind? ActiveTransport = null);

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private static readonly TimeSpan SafetyTick =
        TimeSpan.FromMilliseconds(10);

    private static readonly TimeSpan StatusTick =
        TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan BluetoothRetryDelay =
        TimeSpan.FromMilliseconds(750);

    private readonly SessionId _windowsDeviceId;
    private readonly WindowsTrustedPeerStore _trustedPeers;
    private readonly HidMaestroVirtualGamepadBackend _backend;
    private readonly ControllerSession _controllerSession;
    private readonly InputSafetyEngine _safetyEngine;
    private readonly SmartConnectionManager _connectionManager;
    private readonly object _selectionGate = new();

    private readonly WifiGamepadInputRouter _wifiRouter;
    private readonly WifiControllerTransport _wifiTransport;
    private readonly WifiDiscoveryResponder _discoveryResponder;

    private readonly BluetoothGamepadInputRouter _bluetoothRouter;
    private readonly BluetoothControllerTransport _bluetoothTransport;

    private readonly Channel<RumbleState> _rumbleChannel =
        Channel.CreateBounded<RumbleState>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });

    private CancellationTokenSource? _runtimeCts;
    private Task? _safetyLoop;
    private Task? _statusLoop;
    private Task? _rumbleLoop;
    private Task? _bluetoothReconnectLoop;

    public ReceiverRuntime(
        SessionId windowsDeviceId,
        WindowsTrustedPeerStore trustedPeers)
    {
        _windowsDeviceId = windowsDeviceId;
        _trustedPeers = trustedPeers;

        _backend =
            new HidMaestroVirtualGamepadBackend();

        _controllerSession =
            new ControllerSession();

        _safetyEngine =
            new InputSafetyEngine(
                _controllerSession,
                _backend,
                ConnectionPolicy.Competitive);

        _connectionManager =
            new SmartConnectionManager(
                ConnectionPolicy.Competitive);

        _wifiRouter =
            new WifiGamepadInputRouter(
                _safetyEngine);

        _bluetoothRouter =
            new BluetoothGamepadInputRouter(
                _safetyEngine);

        HelloPayload receiverHello = new(
            DeviceId: _windowsDeviceId,
            Role: DeviceRole.WindowsReceiver,
            Transports:
                TransportMask.Usb |
                TransportMask.Wifi |
                TransportMask.Bluetooth,
            Capabilities:
                CapabilityFlags.Rumble |
                CapabilityFlags.Guide |
                CapabilityFlags.Competitive240Hz |
                CapabilityFlags.WarmStandby,
            MinimumMajor:
                ProtocolVersion.Current.Major,
            MaximumMajor:
                ProtocolVersion.Current.Major,
            MaximumMinor:
                ProtocolVersion.Current.Minor,
            TrustState: TrustState.Paired);

        _wifiTransport =
            new WifiControllerTransport(
                new WifiTransportOptions
                {
                    BindAddress = IPAddress.Any,
                    ListenPort = 37074,
                },
                handshakeFactory: () =>
                    CreateTrustedHandshake(
                        receiverHello));

        var bluetoothConnector =
            new BluetoothRfcommConnector(
                () =>
                    CreateTrustedHandshake(
                        receiverHello));

        _bluetoothTransport =
            new BluetoothControllerTransport(
                bluetoothConnector);

        _wifiTransport.SessionEstablished +=
            OnWifiSessionEstablished;
        _wifiTransport.FrameReceived +=
            OnWifiFrameReceived;

        _bluetoothTransport.SessionEstablished +=
            OnBluetoothSessionEstablished;
        _bluetoothTransport.FrameReceived +=
            OnBluetoothFrameReceived;

        _backend.RumbleReceived +=
            OnRumbleReceived;

        _discoveryResponder =
            new WifiDiscoveryResponder(
                _windowsDeviceId,
                Environment.MachineName,
                controllerPort: 37074);
    }

    public event Action<ReceiverRuntimeStatus>?
        StatusChanged;

    public SessionId WindowsDeviceId =>
        _windowsDeviceId;

    public bool IsRunning =>
        _runtimeCts is not null;

    public async ValueTask StartAsync(
        CancellationToken cancellationToken =
            default)
    {
        if (_runtimeCts is not null)
            return;

        await _backend.StartAsync(
            cancellationToken);

        try
        {
            _discoveryResponder.Start();

            await _wifiTransport.ConnectAsync(
                cancellationToken);
        }
        catch
        {
            await _backend.StopAsync(
                CancellationToken.None);
            throw;
        }

        _runtimeCts =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        _safetyLoop =
            RunSafetyLoopAsync(
                _runtimeCts.Token);

        _statusLoop =
            RunStatusLoopAsync(
                _runtimeCts.Token);

        _rumbleLoop =
            RunRumbleLoopAsync(
                _runtimeCts.Token);

        _bluetoothReconnectLoop =
            RunBluetoothReconnectLoopAsync(
                _runtimeCts.Token);

        PublishStatus(
            "Receiver ready — waiting for trusted controller.");
    }

    public async ValueTask StopAsync(
        CancellationToken cancellationToken =
            default)
    {
        CancellationTokenSource? cts =
            _runtimeCts;

        Task? safetyLoop =
            _safetyLoop;
        Task? statusLoop =
            _statusLoop;
        Task? rumbleLoop =
            _rumbleLoop;
        Task? bluetoothReconnectLoop =
            _bluetoothReconnectLoop;

        _runtimeCts = null;
        _safetyLoop = null;
        _statusLoop = null;
        _rumbleLoop = null;
        _bluetoothReconnectLoop = null;

        if (cts is not null)
            await cts.CancelAsync();

        if (_backend.IsStarted)
            _safetyEngine.ForceNeutral();

        await SendRumbleAsync(
            new RumbleState(0, 0),
            CancellationToken.None);

        await _bluetoothTransport
            .DisconnectAsync(
                CancellationToken.None);

        await _wifiTransport.DisconnectAsync(
            CancellationToken.None);

        await _discoveryResponder.StopAsync();

        if (_backend.IsStarted)
            await _backend.StopAsync(
                cancellationToken);

        foreach (Task? task in new[]
        {
            safetyLoop,
            statusLoop,
            rumbleLoop,
            bluetoothReconnectLoop,
        })
        {
            if (task is null)
                continue;

            try
            {
                await task.WaitAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cts?.Dispose();

        lock (_selectionGate)
        {
            _connectionManager
                .ClearActiveTransport();
            _controllerSession
                .ClearAuthority();
        }

        PublishStatus(
            "Receiver stopped.",
            virtualReady: false,
            wifiListening: false,
            sessionActive: false,
            bluetoothReady: false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(
            CancellationToken.None);

        _wifiTransport.SessionEstablished -=
            OnWifiSessionEstablished;
        _wifiTransport.FrameReceived -=
            OnWifiFrameReceived;

        _bluetoothTransport.SessionEstablished -=
            OnBluetoothSessionEstablished;
        _bluetoothTransport.FrameReceived -=
            OnBluetoothFrameReceived;

        _backend.RumbleReceived -=
            OnRumbleReceived;

        await _bluetoothTransport.DisposeAsync();
        await _wifiTransport.DisposeAsync();
        await _discoveryResponder.DisposeAsync();
        await _backend.DisposeAsync();
    }

    private TrustedReconnectServerHandshake
        CreateTrustedHandshake(
            HelloPayload receiverHello)
    {
        return new TrustedReconnectServerHandshake(
            _windowsDeviceId,
            receiverHello,
            ResolveTrustedPeer,
            monotonicMicros:
                GetMonotonicMicroseconds);
    }

    private TrustedPeerCredentials?
        ResolveTrustedPeer(
            SessionId androidDeviceId)
    {
        return _trustedPeers.TryGet(
            androidDeviceId);
    }

    private void OnWifiSessionEstablished(
        WifiSessionEstablishedInfo established,
        IPEndPoint remoteEndPoint)
    {
        lock (_selectionGate)
        {
            if (_connectionManager.ActiveTransport ==
                TransportKind.Wifi)
            {
                // Same transport, new cryptographic session. Sequence
                // numbering is allowed to restart without recreating the
                // virtual controller.
                _controllerSession.BeginNewSession(
                    TransportKind.Wifi);
            }

            _connectionManager.Report(
                _wifiTransport
                    .GetHealthSnapshot());

            EvaluateTransportSelectionLocked();
        }

        PublishStatus(
            "Trusted Wi-Fi session established.");
    }

    private void OnBluetoothSessionEstablished(
        EstablishedTrustedSession established)
    {
        lock (_selectionGate)
        {
            if (_connectionManager.ActiveTransport ==
                TransportKind.Bluetooth)
            {
                _controllerSession.BeginNewSession(
                    TransportKind.Bluetooth);
            }

            _connectionManager.Report(
                _bluetoothTransport
                    .GetHealthSnapshot());

            EvaluateTransportSelectionLocked();
        }

        PublishStatus(
            "Trusted Bluetooth fallback connected.");
    }

    private void OnWifiFrameReceived(
        ProtocolFrame frame,
        IPEndPoint remoteEndPoint)
    {
        if (frame.MessageType ==
            MessageType.GamepadState)
        {
            _wifiRouter.TryHandle(frame);
        }
    }

    private void OnBluetoothFrameReceived(
        ProtocolFrame frame)
    {
        if (frame.MessageType ==
            MessageType.GamepadState)
        {
            _bluetoothRouter.TryHandle(frame);
        }
    }

    private void OnRumbleReceived(
        RumbleState state)
    {
        _rumbleChannel.Writer
            .TryWrite(state);
    }

    private async Task RunRumbleLoopAsync(
        CancellationToken cancellationToken)
    {
        await foreach (
            RumbleState state in
            _rumbleChannel.Reader
                .ReadAllAsync(
                    cancellationToken))
        {
            await SendRumbleAsync(
                state,
                cancellationToken);
        }
    }

    private async Task SendRumbleAsync(
        RumbleState state,
        CancellationToken cancellationToken)
    {
        TransportKind? active;

        lock (_selectionGate)
            active =
                _connectionManager
                    .ActiveTransport;

        if (active is null)
            return;

        try
        {
            switch (active.Value)
            {
                case TransportKind.Wifi:
                    await SendWifiRumbleAsync(
                        state,
                        cancellationToken);
                    break;

                case TransportKind.Bluetooth:
                    await SendBluetoothRumbleAsync(
                        state,
                        cancellationToken);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is SocketException or
            IOException or
            InvalidOperationException or
            ObjectDisposedException or
            OperationCanceledException)
        {
            // Rumble is best-effort output. Input authority and the
            // controller safety loop must never block on output.
        }
    }

    private async Task SendWifiRumbleAsync(
        RumbleState state,
        CancellationToken cancellationToken)
    {
        IPEndPoint? endpoint =
            _wifiTransport.RemoteEndPoint;

        SessionId sessionId =
            _wifiTransport.ActiveSessionId;

        if (endpoint is null ||
            sessionId == SessionId.Zero ||
            !_wifiTransport
                .HasAuthenticatedSession)
        {
            return;
        }

        await _wifiTransport.SendAsync(
            CreateRumbleFrame(
                sessionId,
                state),
            endpoint,
            cancellationToken);
    }

    private async Task SendBluetoothRumbleAsync(
        RumbleState state,
        CancellationToken cancellationToken)
    {
        SessionId sessionId =
            _bluetoothTransport
                .ActiveSessionId;

        if (sessionId == SessionId.Zero ||
            !_bluetoothTransport
                .HasAuthenticatedSession)
        {
            return;
        }

        await _bluetoothTransport.SendAsync(
            CreateRumbleFrame(
                sessionId,
                state),
            cancellationToken);
    }

    private static ProtocolFrame CreateRumbleFrame(
        SessionId sessionId,
        RumbleState state)
    {
        return new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.Rumble,
            FrameFlags.Authenticated,
            sessionId,
            0,
            GetMonotonicMicroseconds(),
            ControlPayloadCodec.EncodeRumble(
                new RumblePayload(
                    state.LowFrequencyMotor,
                    state.HighFrequencyMotor,
                    0)));
    }

    private async Task RunBluetoothReconnectLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken
            .IsCancellationRequested)
        {
            try
            {
                if (_bluetoothTransport
                        .HasAuthenticatedSession)
                {
                    TransportHealthSnapshot health =
                        _bluetoothTransport
                            .GetHealthSnapshot();

                    if (_bluetoothTransport.State ==
                            TransportRuntimeState.Failed ||
                        health.Grade ==
                            TransportHealthGrade.Lost)
                    {
                        await _bluetoothTransport
                            .DisconnectAsync(
                                CancellationToken.None);
                    }
                }
                else
                {
                    await _bluetoothTransport
                        .ConnectAsync(
                            cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await _bluetoothTransport
                        .DisconnectAsync(
                            CancellationToken.None);
                }
                catch
                {
                }
            }

            try
            {
                await Task.Delay(
                    BluetoothRetryDelay,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunSafetyLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(SafetyTick);

        while (await timer
            .WaitForNextTickAsync(
                cancellationToken))
        {
            _safetyEngine.Evaluate();
        }
    }

    private async Task RunStatusLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(StatusTick);

        while (await timer
            .WaitForNextTickAsync(
                cancellationToken))
        {
            TransportHealthSnapshot wifiHealth =
                _wifiTransport
                    .GetHealthSnapshot();

            TransportHealthSnapshot bluetoothHealth =
                _bluetoothTransport
                    .GetHealthSnapshot();

            lock (_selectionGate)
            {
                _connectionManager.Report(
                    wifiHealth);
                _connectionManager.Report(
                    bluetoothHealth);

                EvaluateTransportSelectionLocked();
            }

            PublishStatus(
                BuildStatusMessage(
                    wifiHealth,
                    bluetoothHealth),
                wifiHealth: wifiHealth,
                bluetoothHealth:
                    bluetoothHealth);
        }
    }

    private void EvaluateTransportSelectionLocked()
    {
        HandoverProposal? proposal =
            _connectionManager.Evaluate();

        if (proposal is null)
            return;

        if (!TransportSessionAvailable(
                proposal.Value.To))
        {
            return;
        }

        // Wi-Fi and Bluetooth currently establish independent encrypted
        // protocol sessions. Switching authority starts the sequence domain
        // of the selected session while the HIDMaestro virtual controller
        // stays alive. No neutral frame is inserted here.
        _controllerSession.BeginNewSession(
            proposal.Value.To);

        _connectionManager.Commit(
            proposal.Value);
    }

    private bool TransportSessionAvailable(
        TransportKind transport)
    {
        return transport switch
        {
            TransportKind.Wifi =>
                _wifiTransport
                    .HasAuthenticatedSession,
            TransportKind.Bluetooth =>
                _bluetoothTransport
                    .HasAuthenticatedSession,
            TransportKind.Usb => false,
            _ => false,
        };
    }

    private string BuildStatusMessage(
        TransportHealthSnapshot wifi,
        TransportHealthSnapshot bluetooth)
    {
        TransportKind? active;

        lock (_selectionGate)
            active =
                _connectionManager
                    .ActiveTransport;

        if (active == TransportKind.Wifi)
        {
            return wifi.Grade is
                TransportHealthGrade.Lost or
                TransportHealthGrade.Critical
                ? "Wi-Fi unstable — evaluating Bluetooth fallback."
                : _bluetoothTransport
                    .HasAuthenticatedSession
                    ? "Controller active over Wi-Fi — Bluetooth ready as backup."
                    : "Controller active over Wi-Fi.";
        }

        if (active == TransportKind.Bluetooth)
        {
            return _wifiTransport
                .HasAuthenticatedSession
                ? "Controller active over Bluetooth — Wi-Fi recovery is being evaluated."
                : "Controller active over Bluetooth fallback.";
        }

        return "Receiver ready — waiting for trusted controller.";
    }

    private void PublishStatus(
        string message,
        bool? virtualReady = null,
        bool? wifiListening = null,
        bool? sessionActive = null,
        TransportHealthSnapshot? wifiHealth = null,
        bool? bluetoothReady = null,
        TransportHealthSnapshot? bluetoothHealth = null)
    {
        TransportKind? active;

        lock (_selectionGate)
            active =
                _connectionManager
                    .ActiveTransport;

        StatusChanged?.Invoke(
            new ReceiverRuntimeStatus(
                Message: message,
                VirtualControllerReady:
                    virtualReady ??
                    _backend.IsStarted,
                WifiListening:
                    wifiListening ??
                    _wifiTransport.State !=
                        TransportRuntimeState
                            .Unavailable,
                SessionActive:
                    sessionActive ??
                    active is not null,
                WifiHealth:
                    wifiHealth,
                BluetoothReady:
                    bluetoothReady ??
                    _bluetoothTransport
                        .HasAuthenticatedSession,
                BluetoothHealth:
                    bluetoothHealth,
                ActiveTransport:
                    active));
    }

    private static ulong
        GetMonotonicMicroseconds()
    {
        long timestamp =
            Stopwatch.GetTimestamp();

        long frequency =
            Stopwatch.Frequency;

        long seconds =
            timestamp / frequency;

        long remainder =
            timestamp % frequency;

        return checked(
            (ulong)seconds *
                1_000_000UL +
            (ulong)(
                remainder *
                1_000_000L /
                frequency));
    }
}
