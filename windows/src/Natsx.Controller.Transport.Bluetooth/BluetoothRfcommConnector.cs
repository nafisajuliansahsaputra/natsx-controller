using Natsx.Controller.Protocol;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothRfcommConnector
{
    private readonly Func<TrustedReconnectServerHandshake> _handshakeFactory;

    public BluetoothRfcommConnector(
        Func<TrustedReconnectServerHandshake> handshakeFactory)
    {
        _handshakeFactory = handshakeFactory ??
            throw new ArgumentNullException(nameof(handshakeFactory));
    }

    public async Task<BluetoothTrustedConnection?> ConnectFirstTrustedAsync(
        CancellationToken cancellationToken = default)
    {
        RfcommServiceId serviceId =
            RfcommServiceId.FromUuid(
                BluetoothRfcommConstants.ServiceUuid);

        string selector =
            RfcommDeviceService.GetDeviceSelector(
                serviceId);

        DeviceInformationCollection services =
            await DeviceInformation.FindAllAsync(
                selector);

        foreach (DeviceInformation information in services)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RfcommDeviceService? service = null;
            StreamSocket? socket = null;

            try
            {
                service =
                    await RfcommDeviceService.FromIdAsync(
                        information.Id);

                if (service is null)
                    continue;

                socket = new StreamSocket();

                await socket.ConnectAsync(
                    service.ConnectionHostName,
                    service.ConnectionServiceName,
                    SocketProtectionLevel
                        .BluetoothEncryptionWithAuthentication);

                Stream input =
                    socket.InputStream.AsStreamForRead(
                        bufferSize: 4096);

                Stream output =
                    socket.OutputStream.AsStreamForWrite(
                        bufferSize: 4096);

                BluetoothTrustedConnection connection =
                    await AuthenticateAsync(
                        socket,
                        service,
                        input,
                        output,
                        cancellationToken);

                socket = null;
                service = null;

                return connection;
            }
            catch (OperationCanceledException)
            {
                socket?.Dispose();
                service?.Dispose();
                throw;
            }
            catch
            {
                socket?.Dispose();
                service?.Dispose();
            }
        }

        return null;
    }

    private async Task<BluetoothTrustedConnection> AuthenticateAsync(
        StreamSocket socket,
        RfcommDeviceService service,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        TrustedReconnectServerHandshake handshake =
            _handshakeFactory();

        try
        {
            ProtocolFrame androidHello =
                await StreamFrameCodec.ReadAsync(
                    input,
                    cancellationToken:
                        cancellationToken);

            IReadOnlyList<ProtocolFrame> replies =
                handshake.HandleHello(
                    androidHello);

            foreach (ProtocolFrame reply in replies)
            {
                await StreamFrameCodec.WriteAsync(
                    output,
                    reply,
                    cancellationToken:
                        cancellationToken);
            }

            ProtocolFrame authResponse =
                await StreamFrameCodec.ReadAsync(
                    input,
                    cancellationToken:
                        cancellationToken);

            ProtocolFrame ready =
                handshake.HandleAuthResponse(
                    authResponse,
                    BluetoothRfcommConstants
                        .ProtocolTransportValue);

            byte[] sessionKey =
                handshake.GetSessionKey();

            try
            {
                await StreamFrameCodec.WriteAsync(
                    output,
                    ready,
                    sessionKey,
                    cancellationToken);

                EstablishedTrustedSession established =
                    handshake.EstablishedSession
                    ?? throw new InvalidOperationException(
                        "Bluetooth trusted reconnect did not establish a session.");

                return new BluetoothTrustedConnection(
                    socket,
                    service,
                    input,
                    output,
                    new EstablishedTrustedSession(
                        established.AndroidDeviceId,
                        established.WindowsDeviceId,
                        established.SessionId,
                        established.SessionKey.ToArray(),
                        established.NegotiatedCapabilities));
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations
                    .ZeroMemory(sessionKey);
            }
        }
        catch
        {
            handshake.Reset();
            input.Dispose();
            output.Dispose();
            throw;
        }
    }
}
