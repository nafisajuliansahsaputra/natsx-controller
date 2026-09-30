using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Wifi;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public sealed record ReceiverRuntimeStatus(
    string Message,
    bool VirtualControllerReady,
    bool WifiListening,
    bool SessionActive,
    TransportHealthSnapshot? WifiHealth = null);

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private static readonly TimeSpan SafetyTick = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan StatusTick = TimeSpan.FromMilliseconds(250);

    private readonly SessionId _windowsDeviceId;
    private readonly WindowsTrustedPeerStore _trustedPeers;
    private readonly HidMaestroVirtualGamepadBackend _backend;
    private readonly ControllerSession _controllerSession;
    private readonly InputSafetyEngine _safetyEngine;
    private readonly WifiGamepadInputRouter _wifiRouter;
    private readonly WifiControllerTransport _wifiTransport;
    private readonly WifiDiscoveryResponder _discoveryResponder;
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

    public ReceiverRuntime(
        SessionId windowsDeviceId,
        WindowsTrustedPeerStore trustedPeers)
    {
        _windowsDeviceId = windowsDeviceId;
        _trustedPeers = trustedPeers;

        _backend = new HidMaestroVirtualGamepadBackend();
        _controllerSession = new ControllerSession();
        _safetyEngine = new InputSafetyEngine(
            _controllerSession,
            _backend,
            ConnectionPolicy.Competitive);

        _wifiRouter = new WifiGamepadInputRouter(_safetyEngine);

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
            MinimumMajor: ProtocolVersion.Current.Major,
            MaximumMajor: ProtocolVersion.Current.Major,
            MaximumMinor: ProtocolVersion.Current.Minor,
            TrustState: TrustState.Paired);

        _wifiTransport = new WifiControllerTransport(
            new WifiTransportOptions
            {
                BindAddress = IPAddress.Any,
                ListenPort = 37074,
            },
            handshakeFactory: () =>
                new TrustedReconnectServerHandshake(
                    _windowsDeviceId,
                    receiverHello,
                    ResolveTrustedPeer,
                    monotonicMicros: GetMonotonicMicroseconds));

        _wifiTransport.SessionEstablished += OnWifiSessionEstablished;
        _wifiTransport.FrameReceived += OnWifiFrameReceived;
        _backend.RumbleReceived += OnRumbleReceived;

        _discoveryResponder = new WifiDiscoveryResponder(
            _windowsDeviceId,
            Environment.MachineName,
            controllerPort: 37074);
    }

    public event Action<ReceiverRuntimeStatus>? StatusChanged;

    public SessionId WindowsDeviceId => _windowsDeviceId;

    public bool IsRunning => _runtimeCts is not null;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is not null)
            return;

        await _backend.StartAsync(cancellationToken);

        try
        {
            _discoveryResponder.Start();
            await _wifiTransport.ConnectAsync(cancellationToken);
        }
        catch
        {
            await _backend.StopAsync(CancellationToken.None);
            throw;
        }

        _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        _safetyLoop = RunSafetyLoopAsync(_runtimeCts.Token);
        _statusLoop = RunStatusLoopAsync(_runtimeCts.Token);
        _rumbleLoop = RunRumbleLoopAsync(_runtimeCts.Token);

        PublishStatus("Receiver ready — waiting for trusted controller.");
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts = _runtimeCts;
        Task? safetyLoop = _safetyLoop;
        Task? statusLoop = _statusLoop;
        Task? rumbleLoop = _rumbleLoop;

        _runtimeCts = null;
        _safetyLoop = null;
        _statusLoop = null;
        _rumbleLoop = null;

        if (cts is not null)
            await cts.CancelAsync();

        if (_backend.IsStarted)
            _safetyEngine.ForceNeutral();

        if (_wifiTransport.HasAuthenticatedSession)
        {
            await SendRumbleAsync(
                new RumbleState(0, 0),
                cancellationToken);
        }

        await _wifiTransport.DisconnectAsync(cancellationToken);
        await _discoveryResponder.StopAsync();

        if (_backend.IsStarted)
            await _backend.StopAsync(cancellationToken);

        foreach (Task? task in new[] { safetyLoop, statusLoop, rumbleLoop })
        {
            if (task is null)
                continue;

            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();

        PublishStatus(
            "Receiver stopped.",
            virtualReady: false,
            wifiListening: false,
            sessionActive: false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);

        _wifiTransport.SessionEstablished -= OnWifiSessionEstablished;
        _wifiTransport.FrameReceived -= OnWifiFrameReceived;
        _backend.RumbleReceived -= OnRumbleReceived;

        await _wifiTransport.DisposeAsync();
        await _discoveryResponder.DisposeAsync();
        await _backend.DisposeAsync();
    }

    private TrustedPeerCredentials? ResolveTrustedPeer(SessionId androidDeviceId)
    {
        return _trustedPeers.TryGet(androidDeviceId);
    }

    private void OnWifiSessionEstablished(
        WifiSessionEstablishedInfo established,
        IPEndPoint remoteEndPoint)
    {
        _controllerSession.BeginNewSession(TransportKind.Wifi);

        PublishStatus(
            "Trusted controller connected over Wi-Fi.",
            sessionActive: true);
    }

    private void OnWifiFrameReceived(
        ProtocolFrame frame,
        IPEndPoint remoteEndPoint)
    {
        if (frame.MessageType == MessageType.GamepadState)
            _wifiRouter.TryHandle(frame);
    }

    private void OnRumbleReceived(RumbleState state)
    {
        _rumbleChannel.Writer.TryWrite(state);
    }

    private async Task RunRumbleLoopAsync(
        CancellationToken cancellationToken)
    {
        await foreach (
            RumbleState state in
            _rumbleChannel.Reader.ReadAllAsync(
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
        IPEndPoint? endpoint =
            _wifiTransport.RemoteEndPoint;
        SessionId sessionId =
            _wifiTransport.ActiveSessionId;

        if (endpoint is null ||
            sessionId == SessionId.Zero ||
            !_wifiTransport.HasAuthenticatedSession)
        {
            return;
        }

        var frame = new ProtocolFrame(
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

        try
        {
            await _wifiTransport.SendAsync(
                frame,
                endpoint,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is SocketException or
            InvalidOperationException or
            OperationCanceledException)
        {
            // Rumble is best-effort output and never blocks input.
        }
    }

    private async Task RunSafetyLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SafetyTick);

        while (await timer.WaitForNextTickAsync(cancellationToken))
            _safetyEngine.Evaluate();
    }

    private async Task RunStatusLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(StatusTick);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            TransportHealthSnapshot health = _wifiTransport.GetHealthSnapshot();

            PublishStatus(
                _wifiTransport.HasAuthenticatedSession
                    ? "Controller session active."
                    : "Receiver ready — waiting for trusted controller.",
                sessionActive: _wifiTransport.HasAuthenticatedSession,
                wifiHealth: health);
        }
    }

    private void PublishStatus(
        string message,
        bool? virtualReady = null,
        bool? wifiListening = null,
        bool? sessionActive = null,
        TransportHealthSnapshot? wifiHealth = null)
    {
        StatusChanged?.Invoke(
            new ReceiverRuntimeStatus(
                Message: message,
                VirtualControllerReady:
                    virtualReady ?? _backend.IsStarted,
                WifiListening:
                    wifiListening ?? _wifiTransport.State != TransportRuntimeState.Unavailable,
                SessionActive:
                    sessionActive ?? _wifiTransport.HasAuthenticatedSession,
                WifiHealth: wifiHealth));
    }

    private static ulong GetMonotonicMicroseconds()
    {
        long timestamp = Stopwatch.GetTimestamp();
        long frequency = Stopwatch.Frequency;

        long seconds = timestamp / frequency;
        long remainder = timestamp % frequency;

        return checked(
            (ulong)seconds * 1_000_000UL +
            (ulong)((remainder * 1_000_000L) / frequency));
    }
}
