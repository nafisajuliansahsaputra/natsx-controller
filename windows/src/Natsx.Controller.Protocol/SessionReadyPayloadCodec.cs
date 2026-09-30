namespace Natsx.Controller.Protocol;

public readonly record struct SessionReadyPayload(
    PeerRole Role,
    TransportCapabilities Capabilities,
    PeerId PeerId);

public static class SessionReadyPayloadCodec
{
    public const int PayloadSize = 20;

    private const TransportCapabilities AllowedCapabilities =
        TransportCapabilities.Wifi |
        TransportCapabilities.Bluetooth |
        TransportCapabilities.UsbDirect;

    public static byte[] Encode(SessionReadyPayload payload)
    {
        Validate(payload);

        var bytes = new byte[PayloadSize];
        Span<byte> span = bytes;

        span[0] = (byte)payload.Role;
        span[1] = (byte)payload.Capabilities;
        span[2] = 0;
        span[3] = 0;
        payload.PeerId.WriteBytes(span[4..20]);

        return bytes;
    }

    public static SessionReadyPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"SESSION_READY payload must be exactly {PayloadSize} bytes.");
        }

        if (bytes[2] != 0 || bytes[3] != 0)
        {
            throw new FormatException(
                "SESSION_READY reserved bytes must be zero.");
        }

        var payload = new SessionReadyPayload(
            (PeerRole)bytes[0],
            (TransportCapabilities)bytes[1],
            PeerId.FromBytes(bytes[4..20]));

        Validate(payload);
        return payload;
    }

    private static void Validate(SessionReadyPayload payload)
    {
        if (!Enum.IsDefined(payload.Role))
        {
            throw new FormatException(
                "SESSION_READY contains an unknown peer role.");
        }

        if ((payload.Capabilities & ~AllowedCapabilities) != 0)
        {
            throw new FormatException(
                "SESSION_READY contains unknown transport capability bits.");
        }
    }
}
