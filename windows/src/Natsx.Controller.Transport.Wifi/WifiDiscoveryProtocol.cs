using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Transport.Wifi;

public sealed record WifiDiscoveryResponse(
    ushort RealtimePort,
    byte[] ReceiverId,
    string ReceiverName);

public static class WifiDiscoveryProtocol
{
    private static ReadOnlySpan<byte> RequestMagic => "NXCDISC1"u8;
    private static ReadOnlySpan<byte> ResponseMagic => "NXCANN01"u8;

    public const int RequestSize = 8;
    public const int MaximumNameBytes = 63;
    public const int FixedResponseSize = 28;

    public static byte[] CreateRequest() => RequestMagic.ToArray();

    public static bool IsRequest(ReadOnlySpan<byte> payload) =>
        payload.SequenceEqual(RequestMagic);

    public static byte[] EncodeResponse(WifiDiscoveryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.ReceiverId.Length != 16)
        {
            throw new ArgumentException("Receiver ID must be exactly 16 bytes.", nameof(response));
        }

        byte[] nameBytes = Encoding.UTF8.GetBytes(response.ReceiverName);
        if (nameBytes.Length is 0 or > MaximumNameBytes)
        {
            throw new ArgumentException($"Receiver name must encode to 1..{MaximumNameBytes} UTF-8 bytes.", nameof(response));
        }

        var output = new byte[FixedResponseSize + nameBytes.Length];
        ResponseMagic.CopyTo(output.AsSpan(0, 8));
        output[8] = 1;
        output[9] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(10, 2), response.RealtimePort);
        response.ReceiverId.CopyTo(output, 12);
        output[28 - 1] = checked((byte)nameBytes.Length);
        nameBytes.CopyTo(output, FixedResponseSize);
        return output;
    }

    public static WifiDiscoveryResponse DecodeResponse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FixedResponseSize)
        {
            throw new FormatException("Discovery response is too short.");
        }

        if (!payload[..8].SequenceEqual(ResponseMagic))
        {
            throw new FormatException("Invalid discovery response magic.");
        }

        if (payload[8] != 1)
        {
            throw new FormatException($"Unsupported discovery major version {payload[8]}.");
        }

        int nameLength = payload[27];
        if (nameLength is < 1 or > MaximumNameBytes || payload.Length != FixedResponseSize + nameLength)
        {
            throw new FormatException("Invalid discovery receiver name length.");
        }

        var receiverId = payload.Slice(12, 16).ToArray();
        string receiverName = Encoding.UTF8.GetString(payload.Slice(FixedResponseSize, nameLength));

        return new WifiDiscoveryResponse(
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2)),
            receiverId,
            receiverName);
    }
}
