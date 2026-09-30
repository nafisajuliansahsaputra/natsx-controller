using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Protocol;

public enum DiscoveryMessageType : byte
{
    Request = 1,
    Response = 2,
}

public readonly record struct DiscoveryRequest(SessionId AndroidDeviceId);

public readonly record struct DiscoveryResponse(
    SessionId WindowsDeviceId,
    ushort ControllerPort,
    string ReceiverName);

public static class DiscoveryCodec
{
    public const int RequestSize = 24;
    public const int ResponseHeaderSize = 26;
    public const int MaximumReceiverNameBytes = 63;

    private static ReadOnlySpan<byte> Magic => "NXD1"u8;

    public static byte[] EncodeRequest(DiscoveryRequest request)
    {
        var bytes = new byte[RequestSize];
        Magic.CopyTo(bytes.AsSpan(0, 4));
        bytes[4] = ProtocolVersion.Current.Major;
        bytes[5] = ProtocolVersion.Current.Minor;
        bytes[6] = (byte)DiscoveryMessageType.Request;
        request.AndroidDeviceId.WriteBytes(bytes.AsSpan(8, 16));
        return bytes;
    }

    public static DiscoveryRequest DecodeRequest(ReadOnlySpan<byte> bytes)
    {
        ValidateCommon(bytes, RequestSize, DiscoveryMessageType.Request);

        if (bytes[7] != 0)
            throw new FormatException("Discovery request reserved byte must be zero.");

        return new DiscoveryRequest(SessionId.FromBytes(bytes[8..24]));
    }

    public static byte[] EncodeResponse(DiscoveryResponse response)
    {
        byte[] name = Encoding.UTF8.GetBytes(response.ReceiverName);

        if (name.Length > MaximumReceiverNameBytes)
            throw new ArgumentOutOfRangeException(nameof(response), "Receiver name is too long.");

        var bytes = new byte[ResponseHeaderSize + name.Length];
        Magic.CopyTo(bytes.AsSpan(0, 4));
        bytes[4] = ProtocolVersion.Current.Major;
        bytes[5] = ProtocolVersion.Current.Minor;
        bytes[6] = (byte)DiscoveryMessageType.Response;
        bytes[7] = (byte)name.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), response.ControllerPort);
        response.WindowsDeviceId.WriteBytes(bytes.AsSpan(10, 16));
        name.CopyTo(bytes, ResponseHeaderSize);
        return bytes;
    }

    public static DiscoveryResponse DecodeResponse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ResponseHeaderSize)
            throw new FormatException("Discovery response is too short.");

        ValidatePrefix(bytes, DiscoveryMessageType.Response);

        int nameLength = bytes[7];
        if (nameLength > MaximumReceiverNameBytes || bytes.Length != ResponseHeaderSize + nameLength)
            throw new FormatException("Discovery response name length is invalid.");

        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]);
        if (port == 0)
            throw new FormatException("Discovery response controller port is invalid.");

        string name = Encoding.UTF8.GetString(bytes.Slice(ResponseHeaderSize, nameLength));

        return new DiscoveryResponse(
            SessionId.FromBytes(bytes[10..26]),
            port,
            name);
    }

    private static void ValidateCommon(
        ReadOnlySpan<byte> bytes,
        int expectedLength,
        DiscoveryMessageType type)
    {
        if (bytes.Length != expectedLength)
            throw new FormatException("Discovery packet length is invalid.");

        ValidatePrefix(bytes, type);
    }

    private static void ValidatePrefix(ReadOnlySpan<byte> bytes, DiscoveryMessageType type)
    {
        if (bytes.Length < 8 || !bytes[..4].SequenceEqual(Magic))
            throw new FormatException("Invalid discovery magic.");

        if (bytes[4] != ProtocolVersion.Current.Major)
            throw new FormatException("Unsupported discovery protocol major.");

        if (bytes[6] != (byte)type)
            throw new FormatException("Unexpected discovery message type.");
    }
}
