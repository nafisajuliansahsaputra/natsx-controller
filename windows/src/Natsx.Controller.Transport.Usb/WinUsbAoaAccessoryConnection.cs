using Windows.Devices.Usb;

namespace Natsx.Controller.Transport.Usb;

public sealed class WinUsbAoaAccessoryConnection : IAsyncDisposable
{
    private readonly UsbDevice _device;
    private bool _disposed;

    internal WinUsbAoaAccessoryConnection(
        UsbDevice device,
        WinUsbAoaAccessoryDevice identity,
        Stream input,
        Stream output)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public WinUsbAoaAccessoryDevice Identity { get; }

    public Stream Input { get; }

    public Stream Output { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await Input.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (!ReferenceEquals(Input, Output))
                {
                    await Output.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _device.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
