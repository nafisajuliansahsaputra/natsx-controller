using Windows.Storage.Streams;

namespace Natsx.Controller.Transport.Usb;

/// <summary>
/// Stream facade that commits every write directly through WinRT DataWriter.
///
/// The standard AsStreamForWrite adapter proved unreliable for the AOA bulk
/// OUT path on the physical OPPO validation device and also exposes an
/// unsupported FlushAsync implementation. This adapter intentionally makes
/// every Write/WriteAsync a complete StoreAsync transaction so protocol
/// control frames cannot remain buffered on the Windows side.
/// </summary>
internal sealed class WinRtUsbDirectOutputStream : Stream
{
    private readonly IOutputStream _output;
    private readonly SemaphoreSlim _writeGate =
        new(1, 1);

    private bool _disposed;

    public WinRtUsbDirectOutputStream(
        IOutputStream output)
    {
        _output =
            output ??
            throw new ArgumentNullException(
                nameof(output));
    }

    public override bool CanRead =>
        false;

    public override bool CanSeek =>
        false;

    public override bool CanWrite =>
        !_disposed;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get =>
            throw new NotSupportedException();
        set =>
            throw new NotSupportedException();
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        // Each write is committed by DataWriter.StoreAsync().
    }

    public override Task FlushAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        // Each write is committed by DataWriter.StoreAsync().
        return Task.CompletedTask;
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(
        long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(
            buffer);

        ValidateRange(
            buffer.Length,
            offset,
            count);

        WriteCoreAsync(
                buffer
                    .AsMemory(
                        offset,
                        count),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            buffer);

        ValidateRange(
            buffer.Length,
            offset,
            count);

        return WriteCoreAsync(
                buffer
                    .AsMemory(
                        offset,
                        count),
                cancellationToken)
            .AsTask();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(
            buffer,
            cancellationToken);

    protected override void Dispose(
        bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed =
            true;

        if (disposing)
        {
            _writeGate.Dispose();
        }

        // UsbDevice owns the native IOutputStream lifetime.
        // Do not close/flush the WinRT stream here.
        base.Dispose(disposing);
    }

    private async ValueTask WriteCoreAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            cancellationToken.ThrowIfCancellationRequested();

            if (buffer.IsEmpty)
            {
                return;
            }

            using var writer =
                new DataWriter(
                    _output);

            writer.WriteBytes(
                buffer.ToArray());

            uint stored =
                await writer
                    .StoreAsync();

            writer.DetachStream();

            if (stored !=
                (uint)buffer.Length)
            {
                throw new IOException(
                    $"WinUSB bulk OUT committed {stored} of {buffer.Length} bytes.");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void ValidateRange(
        int length,
        int offset,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            offset);
        ArgumentOutOfRangeException.ThrowIfNegative(
            count);

        if (offset >
                length ||
            count >
                length - offset)
        {
            throw new ArgumentException(
                "Offset and count exceed the buffer length.");
        }
    }
}
