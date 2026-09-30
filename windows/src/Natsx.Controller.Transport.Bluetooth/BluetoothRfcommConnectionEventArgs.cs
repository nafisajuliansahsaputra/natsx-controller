using Windows.Networking.Sockets;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothRfcommConnectionEventArgs : EventArgs
{
    public BluetoothRfcommConnectionEventArgs(StreamSocket socket)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public StreamSocket Socket { get; }
}
