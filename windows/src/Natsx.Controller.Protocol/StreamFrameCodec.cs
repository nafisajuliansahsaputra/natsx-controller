using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public static class StreamFrameCodec
{
    public const int PrefixSize = 2;

    public const int MinimumFrameSize =
        ProtocolConstants.HeaderSize +
        ProtocolConstants.CrcSize;

    public const int MaximumFrameSize =
        ProtocolConstants.HeaderSize +
        ProtocolConstants.MaximumPayloadSize +
        ProtocolConstants.CrcSize +
        ProtocolConstants.AuthenticationTagSize;

    public static async ValueTask WriteAsync(
        Stream stream,
        ProtocolFrame frame,
        ReadOnlyMemory<byte> authenticationKey = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] encoded = ProtocolFrameCodec.Encode(
            frame,
            authenticationKey.Span);

        if (encoded.Length is < MinimumFrameSize or > MaximumFrameSize)
            throw new InvalidOperationException("Encoded frame length is outside the v1 stream envelope.");

        byte[] prefix = new byte[PrefixSize];
        BinaryPrimitives.WriteUInt16LittleEndian(
            prefix,
            checked((ushort)encoded.Length));

        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(encoded, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<ProtocolFrame> ReadAsync(
        Stream stream,
        ReadOnlyMemory<byte> authenticationKey = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] prefix = new byte[PrefixSize];
        await ReadExactlyOrThrowAsync(
            stream,
            prefix,
            cancellationToken);

        int frameLength =
            BinaryPrimitives.ReadUInt16LittleEndian(prefix);

        if (frameLength is < MinimumFrameSize or > MaximumFrameSize)
        {
            throw new FormatException(
                $"Invalid stream frame length {frameLength}.");
        }

        byte[] frameBytes = new byte[frameLength];

        await ReadExactlyOrThrowAsync(
            stream,
            frameBytes,
            cancellationToken);

        return ProtocolFrameCodec.Decode(
            frameBytes,
            authenticationKey.Span);
    }

    private static async ValueTask ReadExactlyOrThrowAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[total..],
                cancellationToken);

            if (read == 0)
                throw new EndOfStreamException("Stream ended in the middle of a framed protocol message.");

            total += read;
        }
    }
}
