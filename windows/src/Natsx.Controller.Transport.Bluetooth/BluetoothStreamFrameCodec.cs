using System.Buffers.Binary;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth;

public static class BluetoothStreamFrameCodec
{
    public const int LengthPrefixSize = 2;

    public const int MinimumFrameSize =
        ProtocolConstants.HeaderSize +
        ProtocolConstants.CrcSize;

    public const int MaximumFrameSize =
        ProtocolConstants.HeaderSize +
        ProtocolConstants.MaximumPayloadSize +
        ProtocolConstants.CrcSize +
        ProtocolConstants.AuthenticationTagSize;

    public static byte[] Encode(ReadOnlySpan<byte> frame)
    {
        ValidateFrameLength(frame.Length);

        var output =
            new byte[LengthPrefixSize + frame.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(0, LengthPrefixSize),
            checked((ushort)frame.Length));

        frame.CopyTo(
            output.AsSpan(LengthPrefixSize));

        return output;
    }

    public static int DecodeLengthPrefix(
        ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length != LengthPrefixSize)
        {
            throw new ArgumentException(
                $"RFCOMM length prefix must be exactly {LengthPrefixSize} bytes.",
                nameof(prefix));
        }

        int length =
            BinaryPrimitives.ReadUInt16LittleEndian(prefix);

        ValidateFrameLength(length);
        return length;
    }

    public static async ValueTask<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[LengthPrefixSize];

        await stream.ReadExactlyAsync(
            prefix,
            cancellationToken).ConfigureAwait(false);

        int frameLength = DecodeLengthPrefix(prefix);
        var frame = new byte[frameLength];

        await stream.ReadExactlyAsync(
            frame,
            cancellationToken).ConfigureAwait(false);

        return frame;
    }

    private static void ValidateFrameLength(int length)
    {
        if (length < MinimumFrameSize ||
            length > MaximumFrameSize)
        {
            throw new FormatException(
                $"RFCOMM frame length {length} is outside the allowed " +
                $"{MinimumFrameSize}..{MaximumFrameSize} byte range.");
        }
    }
}
