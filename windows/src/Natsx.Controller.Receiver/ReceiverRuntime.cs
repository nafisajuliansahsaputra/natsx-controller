using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Bluetooth;
using Natsx.Controller.Transport.Usb;
using Natsx.Controller.Transport.Wifi;
using Natsx.Controller.Trust.Windows;
using Natsx.Controller.VirtualGamepad;
using Windows.Networking.Sockets;

namespace Natsx.Controller.Receiver;

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private readonly ConnectionPolicy _policy =
        ConnectionPolicy.Competitive;
    private readonly SemaphoreSlim _transportMutationGate =
        new(1, 1);
    private readonly object _pairingGate =
        new();

    private TaskCompletionSource<bool>? _pairingConfirmationSource;

    private CancellationTokenSource? _lifetime;
    private WindowsTrustServices? _trustServices;
    private TrustedSessionRegistry? _sessionRegistry;
    private BluetoothRfcommServiceHost? _bluetoothHost;
    private IDisposable? _bluetoothSessionOwner;
    private StreamSocket? _bluetoothSocket;
    private WifiTrustedControlProcessor? _wifiControlProcessor;
    private WifiDiscoveryResponder? _wifiDiscovery;
    private WifiTrustedSession? _wifiSession;
    private UdpClient? _wifiPairingClient;
    private Task? _wifiPairingTask;
    private Task? _diagnosticsTask;
    private WinUsbAoaAccessoryConnection? _usbConnection;
    private IDisposable? _usbSessionOwner;
    private Task? _usbMonitorTask;
    private HidMaestroVirtualGamepadBackend? _virtualGamepad;
    private ControllerTransportRuntime? _transportRuntime;
    private string _lastHandoverReason =
        "None";
    private bool _started;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public event Action<PairingConfirmationPrompt?>?
        PairingConfirmationChanged;

    public event Action<ReceiverDiagnosticsSnapshot>?
        DiagnosticsChanged;

    public bool IsStarted => _started;

    public bool ResolvePairingConfirmation(
        bool approved)
    {
        TaskCompletionSource<bool>? source;

        lock (_pairingGate)
        {
            source =
                _pairingConfirmationSource;
        }

        return source?.TrySetResult(
            approved) ??
            false;
    }

    public async ValueTask StartAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (_started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        WindowsTrustServices trust =
            WindowsTrustServices.CreateDefault();

        IReadOnlyList<TrustedPeerRecord> trustedPeers =
            trust.TrustedPeers.List();

        if (trustedPeers.Count == 0)
        {
            Report(
                "No trusted controller paired yet. Waiting for secure LAN pairing from Android.");
        }

        var lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        _lifetime = lifetime;
        _trustServices = trust;

        try
        {
            var sessionRegistry =
                new TrustedSessionRegistry();

            _sessionRegistry =
                sessionRegistry;

            var virtualGamepad =
                new HidMaestroVirtualGamepadBackend();

            await virtualGamepad
                .StartAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            _virtualGamepad =
                virtualGamepad;

            var controllerSession =
                new ControllerSession();

            var inputSafety =
                new InputSafetyEngine(
                    controllerSession,
                    virtualGamepad,
                    _policy);

            var smartConnection =
                new SmartConnectionManager(
                    _policy);

            var transportRuntime =
                new ControllerTransportRuntime(
                    controllerSession,
                    inputSafety,
                    smartConnection,
                    Array.Empty<IControllerTransport>(),
                    _policy);

            transportRuntime.HandoverCommitted +=
                proposal =>
                {
                    _lastHandoverReason =
                        $"{proposal.From?.ToString() ?? "None"} -> {proposal.To} ({proposal.Reason})";

                    Report(
                        $"Active transport: {proposal.To}.");
                    PublishDiagnostics();
                };

            transportRuntime.TransportFaulted +=
                (transport, exception) =>
                {
                    Report(
                        $"{transport} transport fault: {exception.Message}");
                    PublishDiagnostics();
                };

            await transportRuntime
                .StartAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            _transportRuntime =
                transportRuntime;
            _started = true;

            _diagnosticsTask =
                DiagnosticsLoopAsync(
                    lifetime.Token);

            PublishDiagnostics();

            await StartWifiHostAsync(
                    trust,
                    sessionRegistry,
                    lifetime.Token)
                .ConfigureAwait(false);

            _wifiPairingTask =
                RunWifiFirstPairingAsync(
                    trust,
                    lifetime.Token);

            await TryStartBluetoothHostAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            _usbMonitorTask =
                UsbMonitorLoopAsync(
                    trust,
                    sessionRegistry,
                    lifetime.Token);

            Report(
                "Receiver ready. Smart Auto is waiting for trusted controller input.");
        }
        catch
        {
            await CleanupAsync()
                .ConfigureAwait(false);

            throw;
        }
    }

    private async ValueTask TryStartBluetoothHostAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await StartBluetoothHostCoreAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(
                $"Bluetooth unavailable: {exception.Message} Wi-Fi and USB remain available.");
        }
    }

    private async ValueTask StartBluetoothHostCoreAsync(
        CancellationToken cancellationToken)
    {
        Report(
            "Starting trusted Bluetooth RFCOMM service…");

        var host =
            new BluetoothRfcommServiceHost();

        host.ConnectionReceived +=
            OnBluetoothConnectionReceived;

        _bluetoothHost =
            host;

        try
        {
            await host
                .StartAsync(
                    radioDiscoverable: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            host.ConnectionReceived -=
                OnBluetoothConnectionReceived;

            await host
                .DisposeAsync()
                .ConfigureAwait(false);

            _bluetoothHost = null;

            throw;
        }

        Report(
            "Bluetooth RFCOMM service ready.");
    }

    private async void OnBluetoothConnectionReceived(
        object? sender,
        BluetoothRfcommConnectionEventArgs eventArgs)
    {
        StreamSocket? socket =
            eventArgs.Socket;
        Stream? inputStream =
            null;
        Stream? outputStream =
            null;
        IDisposable? sessionOwner =
            null;
        bool gateEntered =
            false;
        bool streamOwnershipTransferred =
            false;

        try
        {
            CancellationTokenSource? lifetime =
                _lifetime;
            WindowsTrustServices? trust =
                _trustServices;
            TrustedSessionRegistry? sessionRegistry =
                _sessionRegistry;
            ControllerTransportRuntime? runtime =
                _transportRuntime;

            if (lifetime is null ||
                trust is null ||
                sessionRegistry is null ||
                runtime is null ||
                lifetime.IsCancellationRequested)
            {
                return;
            }

            inputStream =
                socket.InputStream
                    .AsStreamForRead(
                        bufferSize: 0);

            outputStream =
                socket.OutputStream
                    .AsStreamForWrite(
                        bufferSize: 0);

            byte[] firstFrame =
                await BluetoothStreamFrameCodec
                    .ReadFrameAsync(
                        inputStream,
                        lifetime.Token)
                    .ConfigureAwait(false);

            MessageType messageType =
                ReadBluetoothInitialMessageType(
                    firstFrame);

            var lifecycle =
                new TransportLifecycle(
                    TransportRuntimeState.Connecting);

            BluetoothTrustedSession trustedSession;
            PeerId remotePeerId;

            switch (messageType)
            {
                case MessageType.AuthChallenge:
                {
                    var handshakeServer =
                        new BluetoothTrustedHandshakeServer(
                            trust.LocalPeerId,
                            lifecycle: lifecycle,
                            sessionRegistry: sessionRegistry);

                    BluetoothTrustedHandshakeCompletion completion =
                        await handshakeServer
                            .AuthenticateAsync(
                                inputStream,
                                outputStream,
                                peerId =>
                                    ResolveTrustSecret(
                                        trust,
                                        peerId),
                                firstFrame,
                                lifetime.Token)
                            .ConfigureAwait(false);

                    sessionOwner =
                        completion;
                    trustedSession =
                        completion.Session;
                    remotePeerId =
                        completion.RemotePeerId;
                    break;
                }

                case MessageType.SessionReady:
                {
                    var joinServer =
                        new BluetoothSecondarySessionJoinServer(
                            trust.LocalPeerId,
                            sessionRegistry,
                            lifecycle: lifecycle);

                    BluetoothSecondarySessionJoinCompletion completion =
                        await joinServer
                            .JoinAsync(
                                inputStream,
                                outputStream,
                                firstFrame,
                                lifetime.Token)
                            .ConfigureAwait(false);

                    sessionOwner =
                        completion;
                    trustedSession =
                        completion.Session;
                    remotePeerId =
                        completion.RemotePeerId;
                    break;
                }

                default:
                    throw new FormatException(
                        $"Unsupported initial Bluetooth control message: {messageType}.");
            }

            await _transportMutationGate
                .WaitAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            gateEntered =
                true;

            await runtime
                .DetachTransportAsync(
                    TransportKind.Bluetooth,
                    lifetime.Token)
                .ConfigureAwait(false);

            _bluetoothSessionOwner?.Dispose();
            _bluetoothSessionOwner =
                null;

            _bluetoothSocket?.Dispose();
            _bluetoothSocket =
                null;

            var transport =
                new BluetoothControllerTransport(
                    trustedSession,
                    lifecycle,
                    connectionPolicy: _policy);

            bool registered =
                false;

            try
            {
                await runtime
                    .AttachTransportAsync(
                        transport,
                        lifetime.Token)
                    .ConfigureAwait(false);

                registered =
                    true;

                await transport
                    .AttachAuthenticatedStreamAsync(
                        inputStream,
                        outputStream,
                        lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (registered)
                {
                    await runtime
                        .DetachTransportAsync(
                            TransportKind.Bluetooth,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await transport
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }

                throw;
            }

            _bluetoothSessionOwner =
                sessionOwner;
            sessionOwner =
                null;

            _bluetoothSocket =
                socket;
            socket =
                null;

            streamOwnershipTransferred =
                true;

            Report(
                $"Trusted Bluetooth session ready: {remotePeerId}.");
        }
        catch (OperationCanceledException)
            when (_lifetime is null ||
                  _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Report(
                $"Bluetooth session failed: {exception.Message}");
        }
        finally
        {
            if (!streamOwnershipTransferred)
            {
                await DisposeStreamQuietlyAsync(
                        outputStream)
                    .ConfigureAwait(false);

                await DisposeStreamQuietlyAsync(
                        inputStream)
                    .ConfigureAwait(false);
            }

            sessionOwner?.Dispose();
            socket?.Dispose();

            if (gateEntered)
            {
                _transportMutationGate.Release();
            }
        }
    }

    private static MessageType ReadBluetoothInitialMessageType(
        ReadOnlySpan<byte> frame)
    {
        if (frame.Length <
            ProtocolConstants.HeaderSize)
        {
            throw new FormatException(
                "Initial Bluetooth frame is shorter than the protocol header.");
        }

        if (!frame[..ProtocolConstants.Magic.Length]
            .SequenceEqual(
                ProtocolConstants.Magic))
        {
            throw new FormatException(
                "Initial Bluetooth frame has invalid protocol magic.");
        }

        return (MessageType)frame[6];
    }

    private static async ValueTask DisposeStreamQuietlyAsync(
        Stream? stream)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            await stream
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task RunWifiFirstPairingAsync(
        WindowsTrustServices trust,
        CancellationToken cancellationToken)
    {
        var client =
            new UdpClient(
                new IPEndPoint(
                    IPAddress.Any,
                    43858));

        client.EnableBroadcast =
            true;

        _wifiPairingClient =
            client;

        Report(
            "Secure LAN pairing/recovery ready on UDP 43858.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult offerDatagram =
                    await client
                        .ReceiveAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                ProtocolFrame envelope;

                try
                {
                    envelope =
                        ProtocolFrameCodec.Decode(
                            offerDatagram.Buffer);
                }
                catch
                {
                    continue;
                }

                if (envelope.MessageType !=
                    MessageType.PairingOffer)
                {
                    continue;
                }

                PairingOfferPayload offer;

                try
                {
                    offer =
                        PairingFrameCodec
                            .DecodeOffer(
                                offerDatagram.Buffer);
                }
                catch
                {
                    continue;
                }

                using var responder =
                    new PairingResponderSession(
                        trust.LocalPeerId,
                        offer);

                byte[] response =
                    PairingFrameCodec
                        .EncodeResponse(
                            responder.Response);

                await client
                    .SendAsync(
                        response,
                        offerDatagram.RemoteEndPoint,
                        cancellationToken)
                    .ConfigureAwait(false);

                Report(
                    $"LAN pairing request from {offer.AndroidPeerId}. Waiting for matching code confirmation…");

                bool approved =
                    await RequestPairingConfirmationAsync(
                            responder.ComparisonCode,
                            responder.RemotePeerId,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!approved)
                {
                    byte[] abort =
                        PairingFrameCodec
                            .EncodeAbort(
                                new PairingAbortPayload(
                                    PairingAbortReason.UserRejected));

                    await client
                        .SendAsync(
                            abort,
                            offerDatagram.RemoteEndPoint,
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                UdpReceiveResult remoteConfirmationDatagram =
                    await ReceiveFromEndpointAsync(
                            client,
                            offerDatagram.RemoteEndPoint,
                            TimeSpan.FromMinutes(2),
                            cancellationToken)
                        .ConfigureAwait(false);

                ProtocolFrame remoteEnvelope =
                    ProtocolFrameCodec.Decode(
                        remoteConfirmationDatagram.Buffer);

                if (remoteEnvelope.MessageType ==
                    MessageType.PairingAbort)
                {
                    continue;
                }

                if (remoteEnvelope.MessageType !=
                    MessageType.PairingConfirm)
                {
                    continue;
                }

                PairingConfirmPayload remoteConfirmation =
                    PairingFrameCodec
                        .DecodeConfirm(
                            remoteConfirmationDatagram.Buffer);

                PairingConfirmPayload localConfirmation =
                    responder.ApproveDisplayedCode();

                byte[] localConfirmationBytes =
                    PairingFrameCodec
                        .EncodeConfirm(
                            localConfirmation);

                await client
                    .SendAsync(
                        localConfirmationBytes,
                        offerDatagram.RemoteEndPoint,
                        cancellationToken)
                    .ConfigureAwait(false);

                using PairingEstablishedMaterial established =
                    responder.AcceptRemoteConfirmation(
                        remoteConfirmation);

                byte[] secret =
                    established.CopyTrustSecret();

                try
                {
                    IReadOnlyList<TrustedPeerRecord> existingPeers =
                        trust.TrustedPeers.List();

                    foreach (TrustedPeerRecord existingPeer in existingPeers)
                    {
                        if (existingPeer.PeerId !=
                            established.RemotePeerId)
                        {
                            trust.TrustedPeers.Remove(
                                existingPeer.PeerId);
                        }
                    }

                    _sessionRegistry?.Clear();

                    trust.TrustedPeers.Put(
                        new TrustedPeerRecord(
                            established.RemotePeerId,
                            "NATSX Android Controller",
                            (byte)(
                                TransportCapabilities.Wifi |
                                TransportCapabilities.Bluetooth |
                                TransportCapabilities.UsbDirect),
                            DateTimeOffset.UtcNow,
                            TrustedPeerRecord.CurrentPairingVersion),
                        secret);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(
                        secret);
                }

                Report(
                    $"LAN pairing complete. Trusted Android peer: {established.RemotePeerId}. Recovery listener remains active.");
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
        catch (Exception exception)
        {
            Report(
                $"LAN pairing failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(
                    _wifiPairingClient,
                    client))
            {
                _wifiPairingClient =
                    null;
            }

            client.Dispose();
        }
    }

    private static async Task<UdpReceiveResult> ReceiveFromEndpointAsync(
        UdpClient client,
        IPEndPoint expectedEndPoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        timeoutCancellation.CancelAfter(
            timeout);

        while (true)
        {
            UdpReceiveResult result =
                await client
                    .ReceiveAsync(
                        timeoutCancellation.Token)
                    .ConfigureAwait(false);

            if (result.RemoteEndPoint.Equals(
                    expectedEndPoint))
            {
                return result;
            }
        }
    }

    private async ValueTask StartWifiHostAsync(
        WindowsTrustServices trust,
        TrustedSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        Report(
            "Starting trusted Wi-Fi discovery…");

        var controlProcessor =
            new WifiTrustedControlProcessor(
                trust.LocalPeerId,
                sessionRegistry: sessionRegistry);

        var discovery =
            new WifiDiscoveryResponder(
                trust.LocalPeerId,
                trustedControlProcessor: controlProcessor,
                trustSecretResolver:
                    peerId =>
                        ResolveTrustSecret(
                            trust,
                            peerId));

        discovery.TrustedSessionEstablished +=
            OnWifiTrustedSessionEstablished;

        _wifiControlProcessor =
            controlProcessor;
        _wifiDiscovery =
            discovery;

        try
        {
            await discovery
                .StartAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            discovery.TrustedSessionEstablished -=
                OnWifiTrustedSessionEstablished;

            await discovery
                .DisposeAsync()
                .ConfigureAwait(false);

            controlProcessor.Dispose();

            _wifiDiscovery = null;
            _wifiControlProcessor = null;

            throw;
        }

        Report(
            "Wi-Fi discovery ready.");
    }

    private async void OnWifiTrustedSessionEstablished(
        object? sender,
        WifiTrustedSessionEstablishedEventArgs eventArgs)
    {
        WifiTrustedSession? incomingSession =
            eventArgs.Session;
        bool gateEntered =
            false;

        try
        {
            CancellationTokenSource? lifetime =
                _lifetime;

            ControllerTransportRuntime? runtime =
                _transportRuntime;

            if (lifetime is null ||
                runtime is null ||
                lifetime.IsCancellationRequested)
            {
                return;
            }

            await _transportMutationGate
                .WaitAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            gateEntered =
                true;

            if (_wifiSession is not null)
            {
                await runtime
                    .DetachTransportAsync(
                        TransportKind.Wifi,
                        lifetime.Token)
                    .ConfigureAwait(false);

                _wifiSession.Dispose();
                _wifiSession = null;
            }

            var receiver =
                new WifiRealtimeReceiver(
                    incomingSession,
                    connectionPolicy: _policy);

            var transport =
                new WifiControllerTransport(
                    receiver);

            try
            {
                await runtime
                    .AttachTransportAsync(
                        transport,
                        lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                await transport
                    .DisposeAsync()
                    .ConfigureAwait(false);

                throw;
            }

            _wifiSession =
                incomingSession;
            incomingSession =
                null;

            Report(
                $"Trusted Wi-Fi session ready: {eventArgs.RemotePeerId}.");
        }
        catch (OperationCanceledException)
            when (_lifetime is null ||
                  _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Report(
                $"Wi-Fi session failed: {exception.Message}");
        }
        finally
        {
            incomingSession?.Dispose();

            if (gateEntered)
            {
                _transportMutationGate.Release();
            }
        }
    }

    private async Task UsbMonitorLoopAsync(
        WindowsTrustServices trust,
        TrustedSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        int failureCount =
            0;
        string? lastDiagnostic =
            null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ControllerTransportRuntime? runtime =
                    _transportRuntime;

                if (runtime is null)
                {
                    break;
                }

                if (runtime.TryGetTransportState(
                        TransportKind.Usb,
                        out TransportRuntimeState state))
                {
                    bool physicallyPresent =
                        await IsCurrentUsbAccessoryPresentAsync(
                                cancellationToken)
                            .ConfigureAwait(false);

                    if (!physicallyPresent ||
                        state is
                            TransportRuntimeState.Failed or
                            TransportRuntimeState.Unavailable)
                    {
                        await DeactivateUsbCandidateAsync(
                                runtime)
                            .ConfigureAwait(false);

                        failureCount =
                            0;
                        lastDiagnostic =
                            null;

                        Report(
                            runtime.ActiveTransport is TransportKind fallback
                                ? $"USB Direct disconnected. Active transport: {fallback}."
                                : "USB Direct disconnected. Waiting for the best backup transport.");
                    }
                    else
                    {
                        await Task.Delay(
                                TimeSpan.FromMilliseconds(250),
                                cancellationToken)
                            .ConfigureAwait(false);

                        continue;
                    }
                }

                try
                {
                    await StartUsbCandidateCoreAsync(
                            trust,
                            sessionRegistry,
                            cancellationToken)
                        .ConfigureAwait(false);

                    failureCount =
                        0;
                    lastDiagnostic =
                        null;

                    continue;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    failureCount++;

                    if (!string.Equals(
                            lastDiagnostic,
                            exception.Message,
                            StringComparison.Ordinal))
                    {
                        lastDiagnostic =
                            exception.Message;
                    }
                }

                await Task.Delay(
                        GetUsbRetryDelay(
                            failureCount),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failureCount++;

                if (!string.Equals(
                        lastDiagnostic,
                        exception.Message,
                        StringComparison.Ordinal))
                {
                    lastDiagnostic =
                        exception.Message;

                    Report(
                        $"USB monitor recovered from an error: {exception.Message}");
                }

                try
                {
                    await Task.Delay(
                            GetUsbRetryDelay(
                                failureCount),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async ValueTask<bool> IsCurrentUsbAccessoryPresentAsync(
        CancellationToken cancellationToken)
    {
        WinUsbAoaAccessoryConnection? connection =
            _usbConnection;

        if (connection is null)
        {
            return false;
        }

        var backend =
            new WinUsbAoaAccessoryBackend();

        IReadOnlyList<WinUsbAoaAccessoryDevice> devices =
            await backend
                .EnumerateAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        return devices.Any(
            candidate =>
                string.Equals(
                    candidate.DeviceId,
                    connection.Identity.DeviceId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private async ValueTask DeactivateUsbCandidateAsync(
        ControllerTransportRuntime runtime)
    {
        await runtime
            .DetachTransportAsync(
                TransportKind.Usb,
                CancellationToken.None)
            .ConfigureAwait(false);

        _usbSessionOwner?.Dispose();
        _usbSessionOwner =
            null;

        WinUsbAoaAccessoryConnection? connection =
            _usbConnection;

        _usbConnection =
            null;

        if (connection is not null)
        {
            try
            {
                await connection
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static TimeSpan GetUsbRetryDelay(
        int failureCount)
    {
        int seconds =
            failureCount switch
            {
                <= 1 => 1,
                2 => 2,
                3 => 4,
                4 => 8,
                _ => 10,
            };

        return TimeSpan.FromSeconds(
            seconds);
    }

    private async ValueTask StartUsbCandidateCoreAsync(
        WindowsTrustServices trust,
        TrustedSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        ControllerTransportRuntime runtime =
            _transportRuntime ??
            throw new InvalidOperationException(
                "Controller transport runtime is not started.");

        var accessoryBackend =
            new WinUsbAoaAccessoryBackend();

        var bootstrapCoordinator =
            new UsbAoaBootstrapCoordinator(
                accessoryBackend,
                new KmdfAoaBootstrapDeviceProvider());

        UsbBootstrapResult bootstrap =
            await bootstrapCoordinator
                .EnsureAccessoryModeAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        if (!bootstrap.IsReady)
        {
            throw new InvalidOperationException(
                bootstrap.Diagnostic ??
                $"USB bootstrap is not ready ({bootstrap.Status}).");
        }

        Report(
            bootstrap.Status ==
                UsbBootstrapStatus.AccessoryAlreadyReady
                ? "Android USB accessory already ready."
                : "Android USB accessory mode started.");

        WinUsbAoaAccessoryConnection? connection =
            await accessoryBackend
                .OpenFirstAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        if (connection is null)
        {
            throw new InvalidOperationException(
                "Android accessory re-enumerated, but the NATSX WinUSB bulk interface could not be opened.");
        }

        Report(
            $"Native WinUSB data plane ready: IN=0x{connection.BulkInPipeId:X2}, OUT=0x{connection.BulkOutPipeId:X2}, MPS={connection.MaximumPacketSize}.");

        IDisposable? sessionOwner =
            null;
        UsbControllerTransport? transport =
            null;
        bool transportAttached =
            false;

        try
        {
            var lifecycle =
                new TransportLifecycle(
                    TransportRuntimeState.Connecting);

            Report(
                "Waiting for Android USB session…");

            byte[] firstFrame =
                await ReadUsbFrameWithTimeoutAsync(
                        connection.Input,
                        TimeSpan.FromSeconds(15),
                        cancellationToken,
                        "Timed out waiting for Android USB session traffic. Open NATSX Controller on the phone.")
                    .ConfigureAwait(false);

            MessageType messageType =
                ReadUsbInitialMessageType(
                    firstFrame);

            if (messageType ==
                MessageType.PairingOffer)
            {
                await CompleteUsbPairingAsync(
                        connection,
                        trust,
                        firstFrame,
                        cancellationToken)
                    .ConfigureAwait(false);

                firstFrame =
                    await ReadUsbFrameWithTimeoutAsync(
                            connection.Input,
                            TimeSpan.FromSeconds(15),
                            cancellationToken,
                            "Pairing completed, but Android did not start the trusted USB reconnect.")
                        .ConfigureAwait(false);

                messageType =
                    ReadUsbInitialMessageType(
                        firstFrame);
            }

            using var handshakeTimeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            handshakeTimeout.CancelAfter(
                TimeSpan.FromSeconds(15));

            UsbTrustedSession trustedSession;
            PeerId remotePeerId;
            bool uplinkOnly =
                false;

            switch (messageType)
            {
                case MessageType.AuthChallenge:
                {
                    var handshakeServer =
                        new UsbTrustedHandshakeServer(
                            trust.LocalPeerId,
                            lifecycle: lifecycle,
                            sessionRegistry: sessionRegistry);

                    UsbTrustedHandshakeCompletion completion;

                    try
                    {
                        completion =
                            await handshakeServer
                                .AuthenticateAsync(
                                    connection.Input,
                                    connection.Output,
                                    peerId =>
                                        ResolveTrustSecret(
                                            trust,
                                            peerId),
                                    firstFrame,
                                    handshakeTimeout.Token)
                                .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "Timed out completing the trusted Android USB handshake.");
                    }

                    sessionOwner =
                        completion;
                    trustedSession =
                        completion.Session;
                    remotePeerId =
                        completion.RemotePeerId;
                    break;
                }

                case MessageType.SessionReady:
                {
                    var joinServer =
                        new UsbSecondarySessionJoinServer(
                            trust.LocalPeerId,
                            sessionRegistry,
                            lifecycle: lifecycle);

                    UsbSecondarySessionJoinCompletion completion;

                    try
                    {
                        completion =
                            await joinServer
                                .JoinUplinkOnlyAsync(
                                    firstFrame,
                                    handshakeTimeout.Token)
                                .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "Timed out joining USB Direct uplink to the active trusted controller session.");
                    }

                    sessionOwner =
                        completion;
                    trustedSession =
                        completion.Session;
                    remotePeerId =
                        completion.RemotePeerId;
                    uplinkOnly =
                        true;
                    break;
                }

                default:
                    throw new FormatException(
                        $"Unsupported initial USB control message: {messageType}.");
            }

            transport =
                new UsbControllerTransport(
                    trustedSession,
                    lifecycle,
                    connectionPolicy: _policy);

            await runtime
                .AttachTransportAsync(
                    transport,
                    cancellationToken)
                .ConfigureAwait(false);

            transportAttached =
                true;

            try
            {
                if (uplinkOnly)
                {
                    await transport
                        .AttachAuthenticatedStreamAsync(
                            connection.Input,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await transport
                        .AttachAuthenticatedStreamAsync(
                            connection.Input,
                            connection.Output,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                await runtime
                    .DetachTransportAsync(
                        TransportKind.Usb,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                transportAttached =
                    false;
                transport =
                    null;

                throw;
            }

            _usbConnection =
                connection;
            _usbSessionOwner =
                sessionOwner;

            connection =
                null;
            sessionOwner =
                null;
            transport =
                null;

            Report(
                $"USB Direct authenticated and registered with Smart Auto: {remotePeerId}.");
        }
        finally
        {
            if (transportAttached)
            {
                transport =
                    null;
            }

            if (transport is not null)
            {
                await transport
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }

            sessionOwner?.Dispose();

            if (connection is not null)
            {
                await connection
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
    }

    private async ValueTask CompleteUsbPairingAsync(
        WinUsbAoaAccessoryConnection connection,
        WindowsTrustServices trust,
        byte[] firstFrame,
        CancellationToken cancellationToken)
    {
        PairingOfferPayload offer =
            PairingFrameCodec
                .DecodeOffer(
                    firstFrame);

        using var responder =
            new PairingResponderSession(
                trust.LocalPeerId,
                offer);

        Report(
            "Sending pairing response to Android…");

        await WriteUsbFrameAsync(
                connection.Output,
                PairingFrameCodec
                    .EncodeResponse(
                        responder.Response),
                cancellationToken)
            .ConfigureAwait(false);

        Report(
            "Pairing response sent. Waiting for user confirmation…");

        bool approved =
            await RequestPairingConfirmationAsync(
                    responder.ComparisonCode,
                    responder.RemotePeerId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!approved)
        {
            await TrySendPairingAbortAsync(
                    connection.Output,
                    PairingAbortReason.UserRejected,
                    cancellationToken)
                .ConfigureAwait(false);

            throw new InvalidOperationException(
                "Pairing was rejected or timed out on Windows.");
        }

        PairingConfirmPayload localConfirmation =
            responder.ApproveDisplayedCode();

        Report(
            "Windows pairing code confirmed. Sending confirmation to Android…");

        await WriteUsbFrameAsync(
                connection.Output,
                PairingFrameCodec
                    .EncodeConfirm(
                        localConfirmation),
                cancellationToken)
            .ConfigureAwait(false);

        Report(
            "Windows pairing confirmation sent. Waiting for Android confirmation…");

        byte[] remoteFrame =
            await ReadUsbFrameWithTimeoutAsync(
                    connection.Input,
                    TimeSpan.FromMinutes(2),
                    cancellationToken,
                    "Timed out waiting for Android pairing confirmation.")
                .ConfigureAwait(false);

        MessageType remoteType =
            ReadUsbInitialMessageType(
                remoteFrame);

        if (remoteType ==
            MessageType.PairingAbort)
        {
            PairingAbortPayload abort =
                PairingFrameCodec
                    .DecodeAbort(
                        remoteFrame);

            throw new InvalidOperationException(
                $"Android aborted pairing: {abort.Reason}.");
        }

        if (remoteType !=
            MessageType.PairingConfirm)
        {
            throw new FormatException(
                $"Expected PAIRING_CONFIRM from Android, received {remoteType}.");
        }

        Report(
            "Android pairing confirmation received. Verifying…");

        PairingConfirmPayload remoteConfirmation =
            PairingFrameCodec
                .DecodeConfirm(
                    remoteFrame);

        using PairingEstablishedMaterial established =
            responder.AcceptRemoteConfirmation(
                remoteConfirmation);

        byte[] secret =
            established.CopyTrustSecret();

        try
        {
            Report(
                "Android pairing confirmation verified. Saving trust…");

            trust.TrustedPeers.Put(
                new TrustedPeerRecord(
                    established.RemotePeerId,
                    "NATSX Android Controller",
                    (byte)(
                        TransportCapabilities.Wifi |
                        TransportCapabilities.Bluetooth |
                        TransportCapabilities.UsbDirect),
                    DateTimeOffset.UtcNow,
                    TrustedPeerRecord
                        .CurrentPairingVersion),
                secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                secret);
        }

        Report(
            $"Pairing complete. Trusted Android peer: {established.RemotePeerId}.");
    }

    private async ValueTask<bool> RequestPairingConfirmationAsync(
        string comparisonCode,
        PeerId remotePeerId,
        CancellationToken cancellationToken)
    {
        var source =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        lock (_pairingGate)
        {
            if (_pairingConfirmationSource is not null)
            {
                throw new InvalidOperationException(
                    "Another pairing confirmation is already pending.");
            }

            _pairingConfirmationSource =
                source;
        }

        PairingConfirmationChanged?.Invoke(
            new PairingConfirmationPrompt(
                comparisonCode,
                remotePeerId));

        try
        {
            return await source.Task
                .WaitAsync(
                    TimeSpan.FromMinutes(2),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            lock (_pairingGate)
            {
                if (ReferenceEquals(
                        _pairingConfirmationSource,
                        source))
                {
                    _pairingConfirmationSource =
                        null;
                }
            }

            PairingConfirmationChanged?.Invoke(
                null);
        }
    }

    private static async ValueTask WriteUsbFrameAsync(
        Stream output,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        byte[] packet =
            UsbStreamFrameCodec.Encode(
                frame);

        try
        {
            await output.WriteAsync(
                    packet,
                    cancellationToken)
                .ConfigureAwait(false);

            await FlushUsbOutputBestEffortAsync(
                    output,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                frame);
            CryptographicOperations.ZeroMemory(
                packet);
        }
    }

    private static async ValueTask FlushUsbOutputBestEffortAsync(
        Stream output,
        CancellationToken cancellationToken)
    {
        try
        {
            await output.FlushAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            // WinRT USB output adapters may not implement FlushAsync.
            // WriteAsync above is still the authoritative bulk transfer.
        }
        catch (NotSupportedException)
        {
        }
    }

    private static async ValueTask TrySendPairingAbortAsync(
        Stream output,
        PairingAbortReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteUsbFrameAsync(
                    output,
                    PairingFrameCodec
                        .EncodeAbort(
                            new PairingAbortPayload(
                                reason)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async ValueTask<byte[]>
        ReadUsbFrameWithTimeoutAsync(
            Stream input,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string timeoutMessage)
    {
        using var timeoutCancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        timeoutCancellation.CancelAfter(
            timeout);

        try
        {
            return await UsbStreamFrameCodec
                .ReadFrameAsync(
                    input,
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                timeoutMessage);
        }
    }

    private static MessageType ReadUsbInitialMessageType(
        ReadOnlySpan<byte> frame)
    {
        if (frame.Length <
            ProtocolConstants.HeaderSize)
        {
            throw new FormatException(
                "Initial USB frame is shorter than the protocol header.");
        }

        if (!frame[..ProtocolConstants.Magic.Length]
            .SequenceEqual(
                ProtocolConstants.Magic))
        {
            throw new FormatException(
                "Initial USB frame has invalid protocol magic.");
        }

        return (MessageType)frame[6];
    }

    private static byte[]? ResolveTrustSecret(
        WindowsTrustServices trust,
        PeerId peerId)
    {
        using TrustedPeerMaterial? material =
            trust.TrustedPeers.Get(
                peerId);

        if (material is null)
        {
            return null;
        }

        byte[] secret =
            material.CopyTrustSecret();

        if (secret.Length !=
            TrustedReconnectCrypto.TrustKeySize)
        {
            CryptographicOperations.ZeroMemory(
                secret);

            return null;
        }

        return secret;
    }

    private async Task DiagnosticsLoopAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                TimeSpan.FromMilliseconds(
                    500));

        try
        {
            while (await timer
                .WaitForNextTickAsync(
                    cancellationToken)
                .ConfigureAwait(false))
            {
                PublishDiagnostics();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Report(
        string status)
    {
        StatusChanged?.Invoke(
            status);

        PublishDiagnostics();
    }

    private void PublishDiagnostics()
    {
        ControllerTransportRuntime? runtime =
            _transportRuntime;

        TransportKind? active =
            runtime?.ActiveTransport;

        var backup = new List<string>();

        if (runtime is not null)
        {
            foreach (TransportKind kind in Enum.GetValues<TransportKind>())
            {
                if (active == kind)
                {
                    continue;
                }

                if (!runtime.TryGetTransportState(
                        kind,
                        out TransportRuntimeState state))
                {
                    continue;
                }

                if (state is
                    TransportRuntimeState.Ready or
                    TransportRuntimeState.Active or
                    TransportRuntimeState.Stabilizing or
                    TransportRuntimeState.Degraded)
                {
                    backup.Add(
                        $"{kind} ({state})");
                }
            }
        }

        int trustedControllerCount =
            _trustServices?
                .TrustedPeers
                .List()
                .Count ??
            0;

        TransportHealthSnapshot? activeHealth =
            null;
        double inputRateHz =
            0;

        if (runtime is not null &&
            active is TransportKind activeKind)
        {
            if (runtime.TryGetTransportHealthSnapshot(
                    activeKind,
                    out TransportHealthSnapshot health))
            {
                activeHealth =
                    health;
            }

            inputRateHz =
                runtime.GetInputRateHz(
                    activeKind);
        }

        DiagnosticsChanged?.Invoke(
            new ReceiverDiagnosticsSnapshot(
                SmartAutoState:
                    runtime?.State.ToString() ??
                    "Starting",
                ActiveTransport:
                    active?.ToString() ??
                    "None",
                BackupTransports:
                    backup.Count == 0
                        ? "None"
                        : string.Join(
                            ", ",
                            backup),
                VirtualControllerStatus:
                    _virtualGamepad?.IsStarted == true
                        ? "Ready"
                        : "Unavailable",
                TrustedControllerCount:
                    trustedControllerCount,
                RoundTripTime:
                    activeHealth?.RoundTripTime,
                Jitter:
                    activeHealth?.Jitter,
                PacketLossPercent:
                    activeHealth?.PacketLossPercent,
                InputRateHz:
                    inputRateHz,
                ReconnectCount:
                    runtime?.ReconnectCount ??
                    0,
                RecentHandoverReason:
                    _lastHandoverReason));
    }

    private async ValueTask CleanupAsync()
    {
        _started = false;

        CancellationTokenSource? lifetime =
            _lifetime;

        _lifetime = null;
        lifetime?.Cancel();

        Task? diagnosticsTask =
            _diagnosticsTask;

        _diagnosticsTask =
            null;

        if (diagnosticsTask is not null)
        {
            try
            {
                await diagnosticsTask
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        lock (_pairingGate)
        {
            _pairingConfirmationSource
                ?.TrySetCanceled();

            _pairingConfirmationSource =
                null;
        }

        PairingConfirmationChanged?.Invoke(
            null);

        UdpClient? wifiPairingClient =
            _wifiPairingClient;

        _wifiPairingClient =
            null;

        wifiPairingClient?.Dispose();

        Task? wifiPairingTask =
            _wifiPairingTask;

        _wifiPairingTask =
            null;

        if (wifiPairingTask is not null)
        {
            try
            {
                await wifiPairingTask
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        Task? usbMonitor =
            _usbMonitorTask;

        _usbMonitorTask =
            null;

        if (usbMonitor is not null)
        {
            try
            {
                await usbMonitor
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        BluetoothRfcommServiceHost? bluetoothHost =
            _bluetoothHost;

        _bluetoothHost = null;

        if (bluetoothHost is not null)
        {
            bluetoothHost.ConnectionReceived -=
                OnBluetoothConnectionReceived;

            try
            {
                await bluetoothHost
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        WifiDiscoveryResponder? wifiDiscovery =
            _wifiDiscovery;

        _wifiDiscovery = null;

        if (wifiDiscovery is not null)
        {
            wifiDiscovery.TrustedSessionEstablished -=
                OnWifiTrustedSessionEstablished;

            try
            {
                await wifiDiscovery
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await _transportMutationGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            if (_transportRuntime is not null)
            {
                try
                {
                    await _transportRuntime
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                }

                _transportRuntime = null;
            }

            _bluetoothSessionOwner?.Dispose();
            _bluetoothSessionOwner = null;

            _bluetoothSocket?.Dispose();
            _bluetoothSocket = null;

            _wifiSession?.Dispose();
            _wifiSession = null;

            _usbSessionOwner?.Dispose();
            _usbSessionOwner = null;

            if (_usbConnection is not null)
            {
                try
                {
                    await _usbConnection
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                }

                _usbConnection = null;
            }
        }
        finally
        {
            _transportMutationGate.Release();
        }

        _wifiControlProcessor?.Dispose();
        _wifiControlProcessor = null;

        if (_virtualGamepad is not null)
        {
            try
            {
                await _virtualGamepad
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            _virtualGamepad = null;
        }

        _sessionRegistry?.Dispose();
        _sessionRegistry = null;
        _trustServices = null;

        lifetime?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CleanupAsync()
            .ConfigureAwait(false);

        _transportMutationGate.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
