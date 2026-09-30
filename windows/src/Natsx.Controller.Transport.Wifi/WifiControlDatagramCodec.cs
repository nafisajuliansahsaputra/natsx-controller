using System.Buffers.Binary;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public static class WifiControlDatagramCodec
{
    public const int HeartbeatAckPayloadSize = sizeof(ulong);

    public static byte[] EncodeHeartbeat(
        WifiTrustedSession trustedSession,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                sequence: 0,
                monotonicTimestampMicros,
                Array.Empty<byte>()),
            trustedSession.SessionKey);
    }

    public static byte[] EncodeHeartbeatAck(
        WifiTrustedSession trustedSession,
        ulong responderTimestampMicros,
        ulong echoedProbeTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        var payload = new byte[HeartbeatAckPayloadSize];
        BinaryPrimitives.WriteUInt64LittleEndian(
            payload,
            echoedProbeTimestampMicros);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.HeartbeatAck,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                sequence: 0,
                responderTimestampMicros,
                payload),
            trustedSession.SessionKey);
    }

    public static ulong DecodeHeartbeatAck(
        ReadOnlySpan<byte> datagram,
        WifiTrustedSession trustedSession)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(datagram, trustedSession);

        if (frame.MessageType != MessageType.HeartbeatAck)
        {
            throw new FormatException(
                $"Expected {MessageType.HeartbeatAck}, received {frame.MessageType}.");
        }

        if (frame.Payload.Length != HeartbeatAckPayloadSize)
        {
            throw new FormatException(
                "HEARTBEAT_ACK payload must be exactly 8 bytes.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload);
    }

    public static ulong DecodeHeartbeat(
        ReadOnlySpan<byte> datagram,
        WifiTrustedSession trustedSession)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(datagram, trustedSession);

        if (frame.MessageType != MessageType.Heartbeat)
        {
            throw new FormatException(
                $"Expected {MessageType.Heartbeat}, received {frame.MessageType}.");
        }

        if (frame.Payload.Length != 0)
        {
            throw new FormatException("HEARTBEAT payload must be empty.");
        }

        return frame.MonotonicTimestampMicros;
    }

    internal static ProtocolFrame DecodeAuthenticatedFrame(
        ReadOnlySpan<byte> datagram,
        WifiTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        ProtocolFrame frame = ProtocolFrameCodec.Decode(
            datagram,
            trustedSession.SessionKey);

        if (!frame.Flags.HasFlag(FrameFlags.Authenticated))
        {
            throw new CryptographicException(
                "Wi-Fi session traffic must be authenticated.");
        }

        if (frame.SessionId != trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Wi-Fi frame belongs to a different controller session.");
        }

        return frame;
    }
}
