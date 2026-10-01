using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Usb;
using Natsx.Controller.Transport.Wifi;
using Natsx.Controller.Trust.Windows;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private readonly ConnectionPolicy _policy =
        ConnectionPolicy.Competitive;
    private readonly SemaphoreSlim _transportMutationGate =
        new(1, 1);

    private CancellationTokenSource? _lifetime;
    private TrustedSessionRegistry? _sessionRegistry;
    private WifiTrustedControlProcessor? _wifiControlProcessor;
    private WifiDiscoveryResponder? _wifiDiscovery;
    private WifiTrustedSession? _wifiSession;
    private WinUsbAoaAccessoryConnection? _usbConnection;
    private UsbTrustedHandshakeCompletion? _usbHandshake;
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

        UsbTrustedHandshakeCompletion? handshake =
            null;
        UsbControllerTransport? transport =
            null;
        bool transportAttached =
            false;

        try
        {
            var lifecycle =
                new TransportLifecycle();

            Report(
                "Waiting for trusted Android USB handshake…");

            using var handshakeTimeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            handshakeTimeout.CancelAfter(
                TimeSpan.FromSeconds(15));

            var handshakeServer =
                new UsbTrustedHandshakeServer(
                    trust.LocalPeerId,
                    lifecycle: lifecycle,
                    sessionRegistry: sessionRegistry);

            try
            {
                handshake =
                    await handshakeServer
                        .AuthenticateAsync(
                            connection.Input,
                            connection.Output,
                            peerId =>
                                ResolveTrustSecret(
                                    trust,
                                    peerId),
                            handshakeTimeout.Token)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Timed out waiting for the trusted Android USB handshake. Open NATSX Controller on the phone and make sure this PC is paired.");
            }

            transport =
                new UsbControllerTransport(
                    handshake.Session,
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
            _usbHandshake =
                handshake;

            connection =
                null;
            handshake =
                null;
            transport =
                null;

            Report(
                "USB Direct authenticated and registered with Smart Auto.");
        }
        finally
        {
            if (transportAttached)
            {
                // Successful ownership belongs to ControllerTransportRuntime.
                transport =
                    null;
            }

            if (transport is not null)
            {
                await transport
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }

            handshake?.Dispose();

            if (connection is not null)
            {
                await connection
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
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

            _wifiSession?.Dispose();
            _wifiSession = null;

            _usbHandshake?.Dispose();
            _usbHandshake = null;

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
