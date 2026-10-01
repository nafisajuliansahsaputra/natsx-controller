using System.Buffers.Binary;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public static class UsbStreamFrameCodec
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

    public static byte[] Encode(
        ReadOnlySpan<byte> frame)
    {
        ValidateLength(frame.Length);

        var output =
            new byte[
                LengthPrefixSize +
                frame.Length];

        BinaryPrimitives
            .WriteUInt16LittleEndian(
                output.AsSpan(
                    0,
                    LengthPrefixSize),
                checked((ushort)frame.Length));

        frame.CopyTo(
            output.AsSpan(
                LengthPrefixSize));

        return output;
    }

    public static async ValueTask<byte[]> ReadFrameAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        byte[] prefix =
            new byte[LengthPrefixSize];

        await ReadExactlyAsync(
            input,
            prefix,
            cancellationToken)
            .ConfigureAwait(false);

        int length =
            BinaryPrimitives
                .ReadUInt16LittleEndian(
                    prefix);

        ValidateLength(length);

        byte[] frame =
            new byte[length];

        await ReadExactlyAsync(
            input,
            frame,
            cancellationToken)
            .ConfigureAwait(false);

        return frame;
    }

    private static async ValueTask ReadExactlyAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read =
                await input.ReadAsync(
                    buffer[offset..],
                    cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "USB stream ended before a complete frame was available.");
            }

            offset += read;
        }
    }

    private static void ValidateLength(
        int length)
    {
        if (length < MinimumFrameSize ||
            length > MaximumFrameSize)
        {
            throw new InvalidDataException(
                $"USB frame length {length} is outside the allowed " +
                $"{MinimumFrameSize}..{MaximumFrameSize} byte range.");
        }
    }
}
