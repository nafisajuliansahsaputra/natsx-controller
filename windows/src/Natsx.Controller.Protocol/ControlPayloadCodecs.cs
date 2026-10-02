using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public enum ProtocolTransport : byte
{
    Wifi = 1,
    Bluetooth = 2,
    UsbDirect = 3,
}

public enum TransportPreferenceMode : byte
{
    Auto = 0,
    Wifi = 1,
    Bluetooth = 2,
    UsbDirect = 3,
}

public readonly record struct TransportPreferencePayload(
    TransportPreferenceMode Mode);

public static class TransportPreferencePayloadCodec
{
    public const int PayloadSize = 4;

    public static byte[] Encode(
        TransportPreferencePayload payload)
    {
        ValidateMode(payload.Mode);

        return
        [
            (byte)payload.Mode,
            0,
            0,
            0,
        ];
    }

    public static TransportPreferencePayload Decode(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"TRANSPORT_PREFERENCE payload must be exactly {PayloadSize} bytes.");
        }

        TransportReadyPayloadCodec
            .EnsureReservedZero(
                bytes[1..4],
                "TRANSPORT_PREFERENCE");

        var mode =
            (TransportPreferenceMode)bytes[0];

        ValidateMode(mode);

        return new TransportPreferencePayload(
            mode);
    }

    private static void ValidateMode(
        TransportPreferenceMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new FormatException(
                "Unknown transport preference mode.");
        }
    }
}

public readonly record struct HeartbeatAckPayload(ulong EchoedTimestampMicros);

public static class HeartbeatAckPayloadCodec
{
    public const int PayloadSize = 8;

    public static byte[] Encode(HeartbeatAckPayload payload)
    {
        var bytes = new byte[PayloadSize];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, payload.EchoedTimestampMicros);
        return bytes;
    }

    public static HeartbeatAckPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"HEARTBEAT_ACK payload must be exactly {PayloadSize} bytes.");
        }

        return new HeartbeatAckPayload(BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }
}

public readonly record struct TransportReadyPayload(ProtocolTransport Transport);

public static class TransportReadyPayloadCodec
{
    public const int PayloadSize = 4;

    public static byte[] Encode(TransportReadyPayload payload)
    {
        ValidateTransport(payload.Transport);

        return
        [
            (byte)payload.Transport,
            0,
            0,
            0,
        ];
    }

    public static TransportReadyPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"TRANSPORT_READY payload must be exactly {PayloadSize} bytes.");
        }

        EnsureReservedZero(bytes[1..4], "TRANSPORT_READY");
        var transport = (ProtocolTransport)bytes[0];
        ValidateTransport(transport);
        return new TransportReadyPayload(transport);
    }

    internal static void ValidateTransport(ProtocolTransport transport)
    {
        if (!Enum.IsDefined(transport))
        {
            throw new FormatException("Unknown protocol transport.");
        }
    }

    internal static void EnsureReservedZero(ReadOnlySpan<byte> reserved, string messageName)
    {
        foreach (byte value in reserved)
        {
            if (value != 0)
            {
                throw new FormatException($"{messageName} reserved bytes must be zero.");
            }
        }
    }
}

public readonly record struct HandoverPayload(
    ProtocolTransport Transport,
    uint StateSequence);

public static class HandoverPayloadCodec
{
    public const int PayloadSize = 8;

    public static byte[] Encode(HandoverPayload payload)
    {
        TransportReadyPayloadCodec.ValidateTransport(payload.Transport);

        var bytes = new byte[PayloadSize];
        bytes[0] = (byte)payload.Transport;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), payload.StateSequence);
        return bytes;
    }

    public static HandoverPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"Handover payload must be exactly {PayloadSize} bytes.");
        }

        TransportReadyPayloadCodec.EnsureReservedZero(bytes[1..4], "HANDOVER");
        var transport = (ProtocolTransport)bytes[0];
        TransportReadyPayloadCodec.ValidateTransport(transport);

        return new HandoverPayload(
            transport,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]));
    }
}

public readonly record struct RumblePayload(
    byte LowFrequencyMotor,
    byte HighFrequencyMotor);

public static class RumblePayloadCodec
{
    public const int PayloadSize = 4;

    public static byte[] Encode(RumblePayload payload)
    {
        return
        [
            payload.LowFrequencyMotor,
            payload.HighFrequencyMotor,
            0,
            0,
        ];
    }

    public static RumblePayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"RUMBLE payload must be exactly {PayloadSize} bytes.");
        }

        TransportReadyPayloadCodec.EnsureReservedZero(bytes[2..4], "RUMBLE");
        return new RumblePayload(bytes[0], bytes[1]);
    }
}

public enum DisconnectReason : byte
{
    Normal = 0,
    AppStopping = 1,
    TransportClosing = 2,
    ProtocolError = 3,
    AuthenticationFailed = 4,
}

public readonly record struct DisconnectPayload(DisconnectReason Reason);

public static class DisconnectPayloadCodec
{
    public const int PayloadSize = 4;

    public static byte[] Encode(DisconnectPayload payload)
    {
        ValidateReason(payload.Reason);

        return
        [
            (byte)payload.Reason,
            0,
            0,
            0,
        ];
    }

    public static DisconnectPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"DISCONNECT payload must be exactly {PayloadSize} bytes.");
        }

        TransportReadyPayloadCodec.EnsureReservedZero(bytes[1..4], "DISCONNECT");
        var reason = (DisconnectReason)bytes[0];
        ValidateReason(reason);
        return new DisconnectPayload(reason);
    }

    private static void ValidateReason(DisconnectReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new FormatException("Unknown disconnect reason.");
        }
    }
}
