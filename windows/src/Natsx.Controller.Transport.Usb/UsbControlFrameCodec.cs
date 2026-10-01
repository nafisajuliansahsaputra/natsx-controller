using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public static class UsbControlFrameCodec
{
    private const int HeartbeatAckPayloadSize = sizeof(ulong);

    public static byte[] EncodeSessionReady(
        UsbTrustedSession session,
        SessionReadyPayload payload,
        ulong timestampMicros) =>
        ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.SessionReady,
                FrameFlags.Authenticated,
                session.SessionId,
                0,
                timestampMicros,
                SessionReadyPayloadCodec.Encode(payload)),
            session.SessionKey);

    public static SessionReadyPayload DecodeSessionReady(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(frameBytes, session);
        if (frame.MessageType != MessageType.SessionReady)
        {
            throw new FormatException("Expected USB SESSION_READY.");
        }
        return SessionReadyPayloadCodec.Decode(frame.Payload);
    }

    public static byte[] EncodeHeartbeat(
        UsbTrustedSession session,
        ulong timestampMicros) =>
        ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                session.SessionId,
                0,
                timestampMicros,
                Array.Empty<byte>()),
            session.SessionKey);

    public static ulong DecodeHeartbeat(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(frameBytes, session);
        if (frame.MessageType != MessageType.Heartbeat ||
            frame.Payload.Length != 0)
        {
            throw new FormatException("Invalid USB HEARTBEAT.");
        }
        return frame.MonotonicTimestampMicros;
    }

    public static byte[] EncodeHeartbeatAck(
        UsbTrustedSession session,
        ulong responderTimestampMicros,
        ulong echoedProbeTimestampMicros)
    {
        var payload = new byte[HeartbeatAckPayloadSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            payload,
            echoedProbeTimestampMicros);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.HeartbeatAck,
                FrameFlags.Authenticated,
                session.SessionId,
                0,
                responderTimestampMicros,
                payload),
            session.SessionKey);
    }

    public static ulong DecodeHeartbeatAck(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(frameBytes, session);
        if (frame.MessageType != MessageType.HeartbeatAck ||
            frame.Payload.Length != HeartbeatAckPayloadSize)
        {
            throw new FormatException("Invalid USB HEARTBEAT_ACK.");
        }
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload);
    }

    public static byte[] EncodeRumble(
        UsbTrustedSession session,
        RumblePayload payload,
        ulong timestampMicros) =>
        ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Rumble,
                FrameFlags.Authenticated,
                session.SessionId,
                0,
                timestampMicros,
                RumblePayloadCodec.Encode(
                    payload)),
            session.SessionKey);

    public static RumblePayload DecodeRumble(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                session);

        if (frame.MessageType !=
            MessageType.Rumble)
        {
            throw new FormatException(
                $"Expected {MessageType.Rumble}, received {frame.MessageType}.");
        }

        return RumblePayloadCodec.Decode(
            frame.Payload);
    }

    public static byte[] EncodeTransportReady(
        UsbTrustedSession session,
        ProtocolTransport transport,
        ulong timestampMicros) =>
        ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.TransportReady,
                FrameFlags.Authenticated,
                session.SessionId,
                0,
                timestampMicros,
                TransportReadyPayloadCodec.Encode(new TransportReadyPayload(transport))),
            session.SessionKey);

    public static TransportReadyPayload DecodeTransportReady(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame = DecodeAuthenticatedFrame(frameBytes, session);
        if (frame.MessageType != MessageType.TransportReady)
        {
            throw new FormatException("Expected USB TRANSPORT_READY.");
        }
        return TransportReadyPayloadCodec.Decode(frame.Payload);
    }

    private static ProtocolFrame DecodeAuthenticatedFrame(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession session)
    {
        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes, session.SessionKey);

        if (!frame.Flags.HasFlag(FrameFlags.Authenticated) ||
            frame.SessionId != session.SessionId)
        {
            throw new CryptographicException("USB session frame authentication/session mismatch.");
        }

        return frame;
    }
}
