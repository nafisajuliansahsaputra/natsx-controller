using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public enum DeviceRole : byte
{
    AndroidController = 1,
    WindowsReceiver = 2,
}

[Flags]
public enum TransportMask : byte
{
    None = 0,
    Usb = 1 << 0,
    Wifi = 1 << 1,
    Bluetooth = 1 << 2,
}

[Flags]
public enum CapabilityFlags : uint
{
    None = 0,
    Rumble = 1 << 0,
    Guide = 1 << 1,
    Competitive240Hz = 1 << 2,
    WarmStandby = 1 << 3,
}

public enum TrustState : byte
{
    Unpaired = 0,
    Paired = 1,
}

public enum HandoverReason : byte
{
    PreferredTransportReady = 1,
    ActiveTransportDegraded = 2,
    ActiveTransportCriticalOrLost = 3,
    UserOverride = 4,
}

public enum DisconnectReason : byte
{
    NormalShutdown = 1,
    UserDisconnect = 2,
    ProtocolFailure = 3,
    AuthenticationFailure = 4,
    TransportReplaced = 5,
    FatalLocalError = 6,
}

public readonly record struct HelloPayload(
    SessionId DeviceId,
    DeviceRole Role,
    TransportMask Transports,
    CapabilityFlags Capabilities,
    byte MinimumMajor,
    byte MaximumMajor,
    byte MaximumMinor,
    TrustState TrustState);

public readonly record struct SessionReadyPayload(
    byte SelectedMajor,
    byte SelectedMinor,
    byte CurrentTransport,
    CapabilityFlags NegotiatedCapabilities);

public readonly record struct HeartbeatPayload(uint ProbeId);
public readonly record struct TransportReadyPayload(byte Transport);

public readonly record struct HandoverPreparePayload(
    byte CandidateTransport,
    HandoverReason Reason,
    uint ExpectedNextSequence);

public readonly record struct HandoverCommitPayload(
    byte AuthoritativeTransport,
    uint FirstAuthoritativeSequence);

public readonly record struct RumblePayload(
    byte LowFrequency,
    byte HighFrequency,
    ushort DurationMilliseconds);

public readonly record struct DisconnectPayload(DisconnectReason Reason);

public static class ControlPayloadCodec
{
    public const int HelloSize = 32;
    public const int AuthChallengeSize = 32;
    public const int AuthResponseSize = 16;
    public const int SessionReadySize = 8;
    public const int HeartbeatSize = 8;
    public const int TransportReadySize = 4;
    public const int HandoverSize = 8;
    public const int RumbleSize = 4;
    public const int DisconnectSize = 4;

    private const TransportMask AllowedTransports =
        TransportMask.Usb | TransportMask.Wifi | TransportMask.Bluetooth;

    private const CapabilityFlags AllowedCapabilities =
        CapabilityFlags.Rumble |
        CapabilityFlags.Guide |
        CapabilityFlags.Competitive240Hz |
        CapabilityFlags.WarmStandby;

    public static byte[] EncodeHello(HelloPayload payload)
    {
        ValidateEnum(payload.Role);
        ValidateEnum(payload.TrustState);

        if ((payload.Transports & ~AllowedTransports) != 0)
            throw new ArgumentOutOfRangeException(nameof(payload));

        if ((payload.Capabilities & ~AllowedCapabilities) != 0)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var bytes = new byte[HelloSize];
        payload.DeviceId.WriteBytes(bytes.AsSpan(0, 16));
        bytes[16] = (byte)payload.Role;
        bytes[17] = (byte)payload.Transports;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18, 4), (uint)payload.Capabilities);
        bytes[22] = payload.MinimumMajor;
        bytes[23] = payload.MaximumMajor;
        bytes[24] = payload.MaximumMinor;
        bytes[25] = (byte)payload.TrustState;
        return bytes;
    }

    public static HelloPayload DecodeHello(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, HelloSize);
        RequireReservedZero(bytes[26..32], "HELLO");

        var role = (DeviceRole)bytes[16];
        var trust = (TrustState)bytes[25];
        ValidateEnum(role);
        ValidateEnum(trust);

        var transports = (TransportMask)bytes[17];
        if ((transports & ~AllowedTransports) != 0)
            throw new FormatException("HELLO contains unknown transport bits.");

        var capabilities = (CapabilityFlags)BinaryPrimitives.ReadUInt32LittleEndian(bytes[18..22]);
        if ((capabilities & ~AllowedCapabilities) != 0)
            throw new FormatException("HELLO contains unknown capability bits.");

        return new HelloPayload(
            SessionId.FromBytes(bytes[..16]),
            role,
            transports,
            capabilities,
            bytes[22],
            bytes[23],
            bytes[24],
            trust);
    }

    public static byte[] EncodeSessionReady(SessionReadyPayload payload)
    {
        if ((payload.NegotiatedCapabilities & ~AllowedCapabilities) != 0)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var bytes = new byte[SessionReadySize];
        bytes[0] = payload.SelectedMajor;
        bytes[1] = payload.SelectedMinor;
        bytes[2] = payload.CurrentTransport;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)payload.NegotiatedCapabilities);
        return bytes;
    }

    public static SessionReadyPayload DecodeSessionReady(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, SessionReadySize);
        if (bytes[3] != 0)
            throw new FormatException("SESSION_READY reserved flags must be zero.");

        var capabilities = (CapabilityFlags)BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]);
        if ((capabilities & ~AllowedCapabilities) != 0)
            throw new FormatException("SESSION_READY contains unknown capability bits.");

        return new SessionReadyPayload(bytes[0], bytes[1], bytes[2], capabilities);
    }

    public static byte[] EncodeHeartbeat(HeartbeatPayload payload)
    {
        var bytes = new byte[HeartbeatSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), payload.ProbeId);
        return bytes;
    }

    public static HeartbeatPayload DecodeHeartbeat(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, HeartbeatSize);
        RequireReservedZero(bytes[4..8], "HEARTBEAT");
        return new HeartbeatPayload(BinaryPrimitives.ReadUInt32LittleEndian(bytes[..4]));
    }

    public static byte[] EncodeTransportReady(TransportReadyPayload payload) =>
        new[] { payload.Transport, (byte)0, (byte)0, (byte)0 };

    public static TransportReadyPayload DecodeTransportReady(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, TransportReadySize);
        RequireReservedZero(bytes[1..4], "TRANSPORT_READY");
        return new TransportReadyPayload(bytes[0]);
    }

    public static byte[] EncodeHandoverPrepare(HandoverPreparePayload payload)
    {
        ValidateEnum(payload.Reason);
        var bytes = new byte[HandoverSize];
        bytes[0] = payload.CandidateTransport;
        bytes[1] = (byte)payload.Reason;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), payload.ExpectedNextSequence);
        return bytes;
    }

    public static HandoverPreparePayload DecodeHandoverPrepare(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, HandoverSize);
        RequireReservedZero(bytes[2..4], "HANDOVER_PREPARE");
        var reason = (HandoverReason)bytes[1];
        ValidateEnum(reason);

        return new HandoverPreparePayload(
            bytes[0],
            reason,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]));
    }

    public static byte[] EncodeHandoverCommit(HandoverCommitPayload payload)
    {
        var bytes = new byte[HandoverSize];
        bytes[0] = payload.AuthoritativeTransport;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), payload.FirstAuthoritativeSequence);
        return bytes;
    }

    public static HandoverCommitPayload DecodeHandoverCommit(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, HandoverSize);
        RequireReservedZero(bytes[1..4], "HANDOVER_COMMIT");

        return new HandoverCommitPayload(
            bytes[0],
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]));
    }

    public static byte[] EncodeRumble(RumblePayload payload)
    {
        var bytes = new byte[RumbleSize];
        bytes[0] = payload.LowFrequency;
        bytes[1] = payload.HighFrequency;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), payload.DurationMilliseconds);
        return bytes;
    }

    public static RumblePayload DecodeRumble(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, RumbleSize);

        return new RumblePayload(
            bytes[0],
            bytes[1],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..4]));
    }

    public static byte[] EncodeDisconnect(DisconnectPayload payload)
    {
        ValidateEnum(payload.Reason);
        return new[] { (byte)payload.Reason, (byte)0, (byte)0, (byte)0 };
    }

    public static DisconnectPayload DecodeDisconnect(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, DisconnectSize);
        RequireReservedZero(bytes[1..4], "DISCONNECT");
        var reason = (DisconnectReason)bytes[0];
        ValidateEnum(reason);
        return new DisconnectPayload(reason);
    }

    public static byte[] ValidateAuthChallenge(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, AuthChallengeSize);
        return bytes.ToArray();
    }

    public static byte[] ValidateAuthResponse(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, AuthResponseSize);
        return bytes.ToArray();
    }

    private static void RequireLength(ReadOnlySpan<byte> bytes, int expected)
    {
        if (bytes.Length != expected)
            throw new FormatException($"Expected payload length {expected}, got {bytes.Length}.");
    }

    private static void RequireReservedZero(ReadOnlySpan<byte> bytes, string name)
    {
        foreach (byte value in bytes)
        {
            if (value != 0)
                throw new FormatException($"{name} reserved bytes must be zero.");
        }
    }

    private static void ValidateEnum<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new FormatException($"Unknown {typeof(T).Name} value.");
    }
}
