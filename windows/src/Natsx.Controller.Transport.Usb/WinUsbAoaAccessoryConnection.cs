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

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            DisposeStream(Input);

            if (!ReferenceEquals(Input, Output))
            {
                DisposeStream(Output);
            }
        }
        finally
        {
            _device.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static void DisposeStream(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (NotImplementedException)
        {
            // Some WinRT USB output streams do not implement FlushAsync.
            // The underlying UsbDevice is disposed below and owns the native
            // pipe lifetime, so unsupported flush-on-close must not turn a
            // successful USB session into a teardown failure.
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
