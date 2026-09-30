using Natsx.Controller.Protocol;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothTrustedConnection : IAsyncDisposable
{
    private readonly StreamSocket _socket;
    private readonly RfcommDeviceService _service;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private byte[] _sessionKey;
    private bool _disposed;

    internal BluetoothTrustedConnection(
        StreamSocket socket,
        RfcommDeviceService service,
        Stream input,
        Stream output,
        EstablishedTrustedSession session)
    {
        _socket = socket;
        _service = service;
        _input = input;
        _output = output;
        Session = session;
        _sessionKey = session.SessionKey.ToArray();
    }

    public EstablishedTrustedSession Session { get; }

    public async ValueTask<ProtocolFrame> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await StreamFrameCodec.ReadAsync(
            _input,
            _sessionKey,
            cancellationToken);
    }

    public async ValueTask WriteAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeGate.WaitAsync(cancellationToken);

        try
        {
            await StreamFrameCodec.WriteAsync(
                _output,
                frame,
                _sessionKey,
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _writeGate.WaitAsync();

        try
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                _sessionKey);
            _sessionKey = Array.Empty<byte>();

            if (Session.SessionKey.Length > 0)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                    Session.SessionKey);
            }

            _input.Dispose();
            _output.Dispose();
            _socket.Dispose();
            _service.Dispose();
        }
        finally
        {
            _writeGate.Release();
            _writeGate.Dispose();
        }
    }
}
