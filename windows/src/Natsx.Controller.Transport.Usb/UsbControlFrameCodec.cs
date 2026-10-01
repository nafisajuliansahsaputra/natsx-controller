using System.Buffers.Binary;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public static class UsbControlFrameCodec
{
    public const int HeartbeatAckPayloadSize =
        sizeof(ulong);

    public static byte[] EncodeSessionReady(
        UsbTrustedSession trustedSession,
        SessionReadyPayload payload,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(
            trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.SessionReady,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                monotonicTimestampMicros,
                SessionReadyPayloadCodec.Encode(
                    payload)),
            trustedSession.SessionKey);
    }

    public static SessionReadyPayload DecodeSessionReady(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession trustedSession)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                trustedSession);

        if (frame.MessageType !=
            MessageType.SessionReady)
        {
            throw new FormatException(
                "Expected USB SESSION_READY frame.");
        }

        return SessionReadyPayloadCodec.Decode(
            frame.Payload);
    }

    public static byte[] EncodeTransportReady(
        UsbTrustedSession trustedSession,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(
            trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.TransportReady,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                monotonicTimestampMicros,
                TransportReadyPayloadCodec.Encode(
                    new TransportReadyPayload(
                        ProtocolTransport.UsbDirect))),
            trustedSession.SessionKey);
    }

    public static TransportReadyPayload DecodeTransportReady(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession trustedSession)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                trustedSession);

        if (frame.MessageType !=
            MessageType.TransportReady)
        {
            throw new FormatException(
                "Expected USB TRANSPORT_READY frame.");
        }

        return TransportReadyPayloadCodec.Decode(
            frame.Payload);
    }

    public static byte[] EncodeHeartbeat(
        UsbTrustedSession trustedSession,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(
            trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                monotonicTimestampMicros,
                []),
            trustedSession.SessionKey);
    }

    public static ulong DecodeHeartbeatAck(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession trustedSession)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                trustedSession);

        if (frame.MessageType !=
            MessageType.HeartbeatAck)
        {
            throw new FormatException(
                "Expected USB HEARTBEAT_ACK frame.");
        }

        if (frame.Payload.Length !=
            HeartbeatAckPayloadSize)
        {
            throw new FormatException(
                "USB HEARTBEAT_ACK payload must be exactly 8 bytes.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(
            frame.Payload);
    }

    internal static ProtocolFrame DecodeAuthenticatedFrame(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(
            trustedSession);

        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                frameBytes,
                trustedSession.SessionKey);

        if (!frame.Flags.HasFlag(
                FrameFlags.Authenticated))
        {
            throw new CryptographicException(
                "USB session traffic must be authenticated.");
        }

        if (frame.SessionId !=
            trustedSession.SessionId)
        {
            throw new CryptographicException(
                "USB frame belongs to a different controller session.");
        }

        return frame;
    }
}
