namespace Natsx.Controller.Transport.Usb;

public sealed class WinUsbAoaAccessoryConnection : IAsyncDisposable
{
    private readonly IDisposable _owner;
    private bool _disposed;

    internal WinUsbAoaAccessoryConnection(
        IDisposable owner,
        WinUsbAoaAccessoryDevice identity,
        Stream input,
        Stream output,
        byte bulkInPipeId,
        byte bulkOutPipeId,
        ushort maximumPacketSize)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        BulkInPipeId = bulkInPipeId;
        BulkOutPipeId = bulkOutPipeId;
        MaximumPacketSize = maximumPacketSize;
    }

    public WinUsbAoaAccessoryDevice Identity { get; }

    public Stream Input { get; }

    public Stream Output { get; }

    public byte BulkInPipeId { get; }

    public byte BulkOutPipeId { get; }

    public ushort MaximumPacketSize { get; }

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
            _owner.Dispose();
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
