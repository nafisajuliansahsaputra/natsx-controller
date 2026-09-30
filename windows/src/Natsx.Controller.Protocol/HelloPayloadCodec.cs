using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public enum PeerRole : byte
{
    AndroidController = 1,
    WindowsReceiver = 2,
}

[Flags]
public enum TransportCapabilities : byte
{
    None = 0,
    Wifi = 1 << 0,
    Bluetooth = 1 << 1,
    UsbDirect = 1 << 2,
}

public readonly record struct HelloPayload(
    PeerRole Role,
    TransportCapabilities Capabilities,
    PeerId PeerId,
    ushort RealtimePort,
    uint DiscoveryNonce);

public static class HelloPayloadCodec
{
    public const int PayloadSize = 24;

    private const TransportCapabilities AllowedCapabilities =
        TransportCapabilities.Wifi |
        TransportCapabilities.Bluetooth |
        TransportCapabilities.UsbDirect;

    public static byte[] Encode(HelloPayload payload)
    {
        Validate(payload);

        var bytes = new byte[PayloadSize];
        Span<byte> span = bytes;

        span[0] = (byte)payload.Role;
        span[1] = (byte)payload.Capabilities;
        payload.PeerId.WriteBytes(span[2..18]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[18..20], payload.RealtimePort);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..24], payload.DiscoveryNonce);

        return bytes;
    }

    public static HelloPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"HELLO payload must be exactly {PayloadSize} bytes.");
        }

        var role = (PeerRole)bytes[0];
        var capabilities = (TransportCapabilities)bytes[1];

        var payload = new HelloPayload(
            role,
            capabilities,
            PeerId.FromBytes(bytes[2..18]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..20]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]));

        Validate(payload);
        return payload;
    }

    private static void Validate(HelloPayload payload)
    {
        if (!Enum.IsDefined(payload.Role))
        {
            throw new FormatException("HELLO contains an unknown peer role.");
        }

        if ((payload.Capabilities & ~AllowedCapabilities) != 0)
        {
            throw new FormatException(
                "HELLO contains unknown transport capability bits.");
        }

        if (payload.Role == PeerRole.WindowsReceiver &&
            payload.Capabilities.HasFlag(TransportCapabilities.Wifi) &&
            payload.RealtimePort == 0)
        {
            throw new FormatException(
                "Wi-Fi capable receiver must advertise a realtime UDP port.");
        }
    }
}
