using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Usb;
using Natsx.Controller.Trust.Windows;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public sealed class ReceiverRuntime : IAsyncDisposable
{
    private readonly ConnectionPolicy _policy =
        ConnectionPolicy.Competitive;

    private CancellationTokenSource? _lifetime;
    private TrustedSessionRegistry? _sessionRegistry;
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
            Report(
                "Preparing USB Direct…");

            var accessoryBackend =
                new WinUsbAoaAccessoryBackend();

            var bootstrapCoordinator =
                new UsbAoaBootstrapCoordinator(
                    accessoryBackend,
                    new KmdfAoaBootstrapDeviceProvider());

            UsbBootstrapResult bootstrap =
                await bootstrapCoordinator
                    .EnsureAccessoryModeAsync(
                        lifetime.Token)
                    .ConfigureAwait(false);

            if (!bootstrap.IsReady)
            {
                throw new InvalidOperationException(
                    bootstrap.Diagnostic ??
                    $"USB bootstrap failed with status {bootstrap.Status}.");
            }

            Report(
                bootstrap.Status ==
                    UsbBootstrapStatus.AccessoryAlreadyReady
                    ? "Android accessory already ready."
                    : "Android accessory mode started.");

            WinUsbAoaAccessoryConnection? connection =
                await accessoryBackend
                    .OpenFirstAsync(
                        lifetime.Token)
                    .ConfigureAwait(false);

            if (connection is null)
            {
                throw new InvalidOperationException(
                    "Android accessory re-enumerated, but the NATSX WinUSB bulk interface could not be opened.");
            }

            _usbConnection =
                connection;

            var sessionRegistry =
                new TrustedSessionRegistry();

            _sessionRegistry =
                sessionRegistry;

            var lifecycle =
                new TransportLifecycle();

            Report(
                "Waiting for trusted Android USB handshake…");

            using var handshakeTimeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        lifetime.Token);

            handshakeTimeout.CancelAfter(
                TimeSpan.FromSeconds(15));

            var handshakeServer =
                new UsbTrustedHandshakeServer(
                    trust.LocalPeerId,
                    lifecycle: lifecycle,
                    sessionRegistry: sessionRegistry);

            UsbTrustedHandshakeCompletion handshake;

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
                when (!lifetime.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Timed out waiting for the trusted Android USB handshake. Open NATSX Controller on the phone and make sure this PC is paired.");
            }

            _usbHandshake =
                handshake;

            Report(
                $"Trusted Android peer authenticated: {handshake.RemotePeerId}.");

            var virtualGamepad =
                new HidMaestroVirtualGamepadBackend();

            await virtualGamepad
                .StartAsync(
                    lifetime.Token)
                .ConfigureAwait(false);

            _virtualGamepad =
                virtualGamepad;

            var usbTransport =
                new UsbControllerTransport(
                    handshake.Session,
                    lifecycle,
                    connectionPolicy: _policy);

            await usbTransport
                .AttachAuthenticatedStreamAsync(
                    connection.Input,
                    connection.Output,
                    lifetime.Token)
                .ConfigureAwait(false);

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
                    new[]
                    {
                        usbTransport,
                    },
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

            Report(
                "USB Direct authenticated. Waiting for controller input…");
        }
        catch
        {
            await CleanupAsync()
                .ConfigureAwait(false);

            throw;
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

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
