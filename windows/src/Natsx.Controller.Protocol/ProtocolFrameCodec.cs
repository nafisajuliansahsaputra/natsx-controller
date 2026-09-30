using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public static class ProtocolFrameCodec
{
    private const FrameFlags AllowedFlags = FrameFlags.Authenticated;

    public static byte[] Encode(ProtocolFrame frame, ReadOnlySpan<byte> authenticationKey = default)
    {
        ValidateFrameForEncode(frame, authenticationKey);

        bool authenticated = frame.Flags.HasFlag(FrameFlags.Authenticated);
        int trailerSize = ProtocolConstants.CrcSize +
            (authenticated ? ProtocolConstants.AuthenticationTagSize : 0);

        var output = new byte[ProtocolConstants.HeaderSize + frame.Payload.Length + trailerSize];
        Span<byte> header = output.AsSpan(0, ProtocolConstants.HeaderSize);

        ProtocolConstants.Magic.CopyTo(header[0..4]);
        header[4] = frame.Version.Major;
        header[5] = frame.Version.Minor;
        header[6] = (byte)frame.MessageType;
        header[7] = (byte)frame.Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..10], ProtocolConstants.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..12], checked((ushort)frame.Payload.Length));
        frame.SessionId.WriteBytes(header[12..28]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..32], frame.Sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(header[32..40], frame.MonotonicTimestampMicros);

        frame.Payload.CopyTo(output, ProtocolConstants.HeaderSize);

        int crcOffset = ProtocolConstants.HeaderSize + frame.Payload.Length;
        uint crc = Crc32C.Compute(output.AsSpan(0, crcOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(crcOffset, 4), crc);

        if (authenticated)
        {
            int authenticatedLength = crcOffset + ProtocolConstants.CrcSize;
            using var hmac = new HMACSHA256(authenticationKey.ToArray());
            byte[] hash = hmac.ComputeHash(output, 0, authenticatedLength);
            hash.AsSpan(0, ProtocolConstants.AuthenticationTagSize)
                .CopyTo(output.AsSpan(authenticatedLength, ProtocolConstants.AuthenticationTagSize));
            CryptographicOperations.ZeroMemory(hash);
        }

        return output;
    }

    public static ProtocolFrame Decode(ReadOnlySpan<byte> frameBytes, ReadOnlySpan<byte> authenticationKey = default)
    {
        if (frameBytes.Length < ProtocolConstants.HeaderSize + ProtocolConstants.CrcSize)
        {
            throw new FormatException("Frame is shorter than the minimum v1 frame.");
        }

        if (!frameBytes[0..4].SequenceEqual(ProtocolConstants.Magic))
        {
            throw new FormatException("Invalid protocol magic.");
        }

        var version = new ProtocolVersion(frameBytes[4], frameBytes[5]);
        if (version.Major != ProtocolVersion.Current.Major)
        {
            throw new FormatException($"Unsupported protocol major version {version.Major}.");
        }

        if (!Enum.IsDefined(typeof(MessageType), frameBytes[6]))
        {
            throw new FormatException("Unknown v1 message type.");
        }

        var flags = (FrameFlags)frameBytes[7];
        if ((flags & ~AllowedFlags) != 0)
        {
            throw new FormatException("Unknown v1 frame flags.");
        }

        ushort headerLength = BinaryPrimitives.ReadUInt16LittleEndian(frameBytes[8..10]);
        if (headerLength != ProtocolConstants.HeaderSize)
        {
            throw new FormatException("Invalid v1 header length.");
        }

        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(frameBytes[10..12]);
        if (payloadLength > ProtocolConstants.MaximumPayloadSize)
        {
            throw new FormatException("Payload exceeds the v1 maximum.");
        }

        bool authenticated = flags.HasFlag(FrameFlags.Authenticated);
        int expectedLength =
            ProtocolConstants.HeaderSize +
            payloadLength +
            ProtocolConstants.CrcSize +
            (authenticated ? ProtocolConstants.AuthenticationTagSize : 0);

        if (frameBytes.Length != expectedLength)
        {
            throw new FormatException("Frame length does not match header/trailer fields.");
        }

        int crcOffset = ProtocolConstants.HeaderSize + payloadLength;
        uint suppliedCrc = BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.Slice(crcOffset, 4));
        uint computedCrc = Crc32C.Compute(frameBytes[..crcOffset]);

        if (suppliedCrc != computedCrc)
        {
            throw new FormatException("CRC32C validation failed.");
        }

        if (authenticated)
        {
            if (authenticationKey.Length == 0)
            {
                throw new CryptographicException("Authenticated frame requires a session key.");
            }

            int authenticatedLength = crcOffset + ProtocolConstants.CrcSize;
            Span<byte> expectedTag = stackalloc byte[32];

            using var hmac = new HMACSHA256(authenticationKey.ToArray());
            byte[] hash = hmac.ComputeHash(frameBytes[..authenticatedLength].ToArray());
            hash.CopyTo(expectedTag);
            CryptographicOperations.ZeroMemory(hash);

            if (!CryptographicOperations.FixedTimeEquals(
                    expectedTag[..ProtocolConstants.AuthenticationTagSize],
                    frameBytes.Slice(authenticatedLength, ProtocolConstants.AuthenticationTagSize)))
            {
                CryptographicOperations.ZeroMemory(expectedTag);
                throw new CryptographicException("Frame authentication failed.");
            }

            CryptographicOperations.ZeroMemory(expectedTag);
        }

        byte[] payload = frameBytes
            .Slice(ProtocolConstants.HeaderSize, payloadLength)
            .ToArray();

        return new ProtocolFrame(
            version,
            (MessageType)frameBytes[6],
            flags,
            SessionId.FromBytes(frameBytes[12..28]),
            BinaryPrimitives.ReadUInt32LittleEndian(frameBytes[28..32]),
            BinaryPrimitives.ReadUInt64LittleEndian(frameBytes[32..40]),
            payload);
    }

    private static void ValidateFrameForEncode(
        ProtocolFrame frame,
        ReadOnlySpan<byte> authenticationKey)
    {
        if (frame.Version.Major != ProtocolVersion.Current.Major)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Unsupported protocol major version.");
        }

        if (!Enum.IsDefined(frame.MessageType))
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Unknown message type.");
        }

        if ((frame.Flags & ~AllowedFlags) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Unknown v1 frame flags.");
        }

        if (frame.Payload.Length > ProtocolConstants.MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Payload exceeds the v1 maximum.");
        }

        if (frame.Flags.HasFlag(FrameFlags.Authenticated) && authenticationKey.Length == 0)
        {
            throw new ArgumentException("Authenticated frame requires a session key.", nameof(authenticationKey));
        }
    }
}
