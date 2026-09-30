using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Protocol;

public enum PairingMessageType : byte
{
    PairRequest = 1,
    PairResponse = 2,
    PairConfirm = 3,
    PairComplete = 4,
    PairCancel = 5,
}

public enum PairingRole : byte
{
    Android = 1,
    Windows = 2,
}

public readonly record struct PairingHelloPayload(
    SessionId DeviceId,
    byte[] PublicKeyDer,
    string DisplayName);

public readonly record struct PairingConfirmPayload(
    PairingRole Role,
    byte[] Tag);

public static class PairingCodec
{
    public const int HeaderSize = 8;
    public const int HelloFixedSize = 28;
    public const int ConfirmSize = 28;
    public const int CompleteSize = 24;
    public const int CancelSize = 12;
    public const int MaximumPayloadSize = 2048;
    public const int MaximumPublicKeySize = 512;
    public const int MaximumDisplayNameBytes = 63;
    public const int ConfirmationTagSize = 16;

    private static ReadOnlySpan<byte> Magic => "NXP1"u8;

    public static byte[] EncodeRequest(PairingHelloPayload payload) =>
        EncodeHello(PairingMessageType.PairRequest, payload);

    public static byte[] EncodeResponse(PairingHelloPayload payload) =>
        EncodeHello(PairingMessageType.PairResponse, payload);

    public static PairingHelloPayload DecodeRequest(ReadOnlySpan<byte> bytes) =>
        DecodeHello(PairingMessageType.PairRequest, bytes);

    public static PairingHelloPayload DecodeResponse(ReadOnlySpan<byte> bytes) =>
        DecodeHello(PairingMessageType.PairResponse, bytes);

    public static byte[] EncodeConfirm(PairingConfirmPayload payload)
    {
        if (!Enum.IsDefined(payload.Role))
            throw new ArgumentOutOfRangeException(nameof(payload));

        if (payload.Tag.Length != ConfirmationTagSize)
            throw new ArgumentException("Pairing confirmation tag must be 16 bytes.", nameof(payload));

        var bytes = new byte[ConfirmSize];
        WriteHeader(bytes, PairingMessageType.PairConfirm);
        bytes[8] = (byte)payload.Role;
        payload.Tag.CopyTo(bytes, 12);
        return bytes;
    }

    public static PairingConfirmPayload DecodeConfirm(ReadOnlySpan<byte> bytes)
    {
        ValidateExact(bytes, ConfirmSize, PairingMessageType.PairConfirm);

        if (bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 0)
            throw new FormatException("PAIR_CONFIRM reserved bytes must be zero.");

        var role = (PairingRole)bytes[8];
        if (!Enum.IsDefined(role))
            throw new FormatException("PAIR_CONFIRM role is invalid.");

        return new PairingConfirmPayload(
            role,
            bytes[12..28].ToArray());
    }

    public static byte[] EncodeComplete(ReadOnlySpan<byte> tag)
    {
        if (tag.Length != ConfirmationTagSize)
            throw new ArgumentException("Pairing completion tag must be 16 bytes.", nameof(tag));

        var bytes = new byte[CompleteSize];
        WriteHeader(bytes, PairingMessageType.PairComplete);
        tag.CopyTo(bytes.AsSpan(8, 16));
        return bytes;
    }

    public static byte[] DecodeComplete(ReadOnlySpan<byte> bytes)
    {
        ValidateExact(bytes, CompleteSize, PairingMessageType.PairComplete);
        return bytes[8..24].ToArray();
    }

    public static byte[] EncodeCancel(byte reason)
    {
        var bytes = new byte[CancelSize];
        WriteHeader(bytes, PairingMessageType.PairCancel);
        bytes[8] = reason;
        return bytes;
    }

    public static byte DecodeCancel(ReadOnlySpan<byte> bytes)
    {
        ValidateExact(bytes, CancelSize, PairingMessageType.PairCancel);

        if (bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 0)
            throw new FormatException("PAIR_CANCEL reserved bytes must be zero.");

        return bytes[8];
    }

    public static PairingMessageType PeekType(ReadOnlySpan<byte> bytes)
    {
        ValidateHeader(bytes);

        var type = (PairingMessageType)bytes[6];
        if (!Enum.IsDefined(type))
            throw new FormatException("Unknown pairing message type.");

        return type;
    }

    private static byte[] EncodeHello(
        PairingMessageType type,
        PairingHelloPayload payload)
    {
        if (payload.DeviceId == SessionId.Zero)
            throw new ArgumentException("Pairing Device ID cannot be zero.", nameof(payload));

        if (payload.PublicKeyDer.Length is < 1 or > MaximumPublicKeySize)
            throw new ArgumentOutOfRangeException(nameof(payload), "Pairing public key size is invalid.");

        byte[] nameBytes = Encoding.UTF8.GetBytes(payload.DisplayName ?? string.Empty);

        if (nameBytes.Length > MaximumDisplayNameBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), "Pairing display name is too long.");

        int totalLength = HelloFixedSize + payload.PublicKeyDer.Length + nameBytes.Length;

        if (totalLength > MaximumPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payload), "Pairing payload exceeds maximum size.");

        var bytes = new byte[totalLength];
        WriteHeader(bytes, type);
        payload.DeviceId.WriteBytes(bytes.AsSpan(8, 16));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(24, 2),
            checked((ushort)payload.PublicKeyDer.Length));
        bytes[26] = checked((byte)nameBytes.Length);
        payload.PublicKeyDer.CopyTo(bytes, HelloFixedSize);
        nameBytes.CopyTo(bytes, HelloFixedSize + payload.PublicKeyDer.Length);
        return bytes;
    }

    private static PairingHelloPayload DecodeHello(
        PairingMessageType type,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HelloFixedSize || bytes.Length > MaximumPayloadSize)
            throw new FormatException("Pairing hello payload length is invalid.");

        ValidateHeader(bytes);

        if (bytes[6] != (byte)type)
            throw new FormatException("Unexpected pairing hello message type.");

        if (bytes[27] != 0)
            throw new FormatException("Pairing hello reserved byte must be zero.");

        SessionId deviceId = SessionId.FromBytes(bytes[8..24]);
        if (deviceId == SessionId.Zero)
            throw new FormatException("Pairing Device ID cannot be zero.");

        int publicKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..26]);
        int nameLength = bytes[26];

        if (publicKeyLength is < 1 or > MaximumPublicKeySize)
            throw new FormatException("Pairing public key length is invalid.");

        if (nameLength > MaximumDisplayNameBytes)
            throw new FormatException("Pairing display name length is invalid.");

        int expectedLength = HelloFixedSize + publicKeyLength + nameLength;
        if (bytes.Length != expectedLength)
            throw new FormatException("Pairing hello encoded lengths do not match packet length.");

        byte[] publicKey = bytes
            .Slice(HelloFixedSize, publicKeyLength)
            .ToArray();

        string displayName = Encoding.UTF8.GetString(
            bytes.Slice(HelloFixedSize + publicKeyLength, nameLength));

        return new PairingHelloPayload(
            deviceId,
            publicKey,
            displayName);
    }

    private static void WriteHeader(
        Span<byte> bytes,
        PairingMessageType type)
    {
        Magic.CopyTo(bytes[..4]);
        bytes[4] = ProtocolVersion.Current.Major;
        bytes[5] = ProtocolVersion.Current.Minor;
        bytes[6] = (byte)type;
        bytes[7] = 0;
    }

    private static void ValidateExact(
        ReadOnlySpan<byte> bytes,
        int expectedLength,
        PairingMessageType type)
    {
        if (bytes.Length != expectedLength)
            throw new FormatException("Pairing payload length is invalid.");

        ValidateHeader(bytes);

        if (bytes[6] != (byte)type)
            throw new FormatException("Unexpected pairing message type.");
    }

    private static void ValidateHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new FormatException("Pairing payload is shorter than header.");

        if (!bytes[..4].SequenceEqual(Magic))
            throw new FormatException("Invalid pairing magic.");

        if (bytes[4] != ProtocolVersion.Current.Major)
            throw new FormatException("Unsupported pairing protocol major.");

        if (bytes[7] != 0)
            throw new FormatException("Pairing header reserved byte must be zero.");
    }
}
