namespace Natsx.Controller.Transport.Bluetooth;

/// <summary>
/// Replays a bounded prefix before continuing reads from an existing stream.
/// The inner stream is deliberately left open when this wrapper is disposed so
/// a handshake can inspect/replay its first RFCOMM frame and then hand the
/// original stream to the realtime transport without reopening the socket.
/// </summary>
internal sealed class BluetoothPrefixedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _prefix;
    private int _prefixOffset;
    private bool _disposed;

    public BluetoothPrefixedReadStream(
        ReadOnlySpan<byte> prefix,
        Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        if (!inner.CanRead)
        {
            throw new ArgumentException(
                "Bluetooth prefixed stream requires a readable inner stream.",
                nameof(inner));
        }

        _prefix = prefix.ToArray();
        _inner = inner;
    }

    public override bool CanRead =>
        !_disposed &&
        _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get =>
            throw new NotSupportedException();
        set =>
            throw new NotSupportedException();
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(buffer);

        return Read(
            buffer.AsSpan(
                offset,
                count));
    }

    public override int Read(
        Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (_prefixOffset < _prefix.Length)
        {
            int count =
                Math.Min(
                    buffer.Length,
                    _prefix.Length -
                    _prefixOffset);

            _prefix
                .AsSpan(
                    _prefixOffset,
                    count)
                .CopyTo(buffer);

            _prefixOffset +=
                count;

            return count;
        }

        return _inner.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        if (_prefixOffset < _prefix.Length)
        {
            int count =
                Math.Min(
                    buffer.Length,
                    _prefix.Length -
                    _prefixOffset);

            _prefix
                .AsMemory(
                    _prefixOffset,
                    count)
                .CopyTo(buffer);

            _prefixOffset +=
                count;

            return count;
        }

        return await _inner
            .ReadAsync(
                buffer,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return ReadAsync(
                buffer.AsMemory(
                    offset,
                    count),
                cancellationToken)
            .AsTask();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(
        long offset,
        SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(
        long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(
        bool disposing)
    {
        _disposed = true;

        base.Dispose(disposing);
    }
}
