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

    private CancellationTokenSource? _lifetime;
    private WindowsTrustServices? _trustServices;
    private TrustedSessionRegistry? _sessionRegistry;
    private BluetoothRfcommServiceHost? _bluetoothHost;
    private IDisposable? _bluetoothSessionOwner;
    private StreamSocket? _bluetoothSocket;
    private WifiTrustedControlProcessor? _wifiControlProcessor;
    private WifiDiscoveryResponder? _wifiDiscovery;
    private WifiTrustedSession? _wifiSession;
    private WinUsbAoaAccessoryConnection? _usbConnection;
    private IDisposable? _usbSessionOwner;
    private HidMaestroVirtualGamepadBackend? _virtualGamepad;
    private ControllerTransportRuntime? _transportRuntime;
    private bool _started;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public bool IsStarted => _started;

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
            throw new InvalidOperationException(
                "No trusted Android controller is paired with this Windows receiver yet.");
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
                    Report(
                        $"Active transport: {proposal.To}.");

            transportRuntime.TransportFaulted +=
                (transport, exception) =>
                    Report(
                        $"{transport} transport fault: {exception.Message}");

            await transportRuntime
                .StartAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            _transportRuntime =
                transportRuntime;
            _started = true;

            await StartWifiHostAsync(
                    trust,
                    sessionRegistry,
                    lifetime.Token)
                .ConfigureAwait(false);

            await TryStartBluetoothHostAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            await TryStartUsbCandidateAsync(
                    trust,
                    sessionRegistry,
                    lifetime.Token)
                .ConfigureAwait(false);

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

    private async ValueTask TryStartUsbCandidateAsync(
        WindowsTrustServices trust,
        TrustedSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartUsbCandidateCoreAsync(
                    trust,
                    sessionRegistry,
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
                $"USB Direct unavailable: {exception.Message} Wi-Fi remains available.");
        }
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

        Report(
            "Checking USB Direct…");

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
                "Waiting for trusted Android USB session…");

            using var handshakeTimeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            handshakeTimeout.CancelAfter(
                TimeSpan.FromSeconds(15));

            byte[] firstFrame;

            try
            {
                firstFrame =
                    await UsbStreamFrameCodec
                        .ReadFrameAsync(
                            connection.Input,
                            handshakeTimeout.Token)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Timed out waiting for the trusted Android USB session. Open NATSX Controller on the phone and make sure this PC is paired.");
            }

            MessageType messageType =
                ReadUsbInitialMessageType(
                    firstFrame);

            UsbTrustedSession trustedSession;
            PeerId remotePeerId;

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
                                .JoinAsync(
                                    connection.Input,
                                    connection.Output,
                                    firstFrame,
                                    handshakeTimeout.Token)
                                .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "Timed out joining USB Direct to the active trusted controller session.");
                    }

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
                await transport
                    .AttachAuthenticatedStreamAsync(
                        connection.Input,
                        connection.Output,
                        cancellationToken)
                    .ConfigureAwait(false);
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

    private void Report(
        string status)
    {
        StatusChanged?.Invoke(
            status);
    }

    private async ValueTask CleanupAsync()
    {
        _started = false;

        CancellationTokenSource? lifetime =
            _lifetime;

        _lifetime = null;
        lifetime?.Cancel();

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
