namespace Natsx.Controller.Protocol;

public enum AuthenticationMode : byte
{
    FirstPairing = 1,
    TrustedReconnect = 2,
}

public sealed record AuthChallengePayload(
    AuthenticationMode Mode,
    PeerId SenderPeerId,
    byte[] Nonce,
    SessionId SessionId,
    byte[] EphemeralPublicKey);

public sealed record AuthResponsePayload(
    AuthenticationMode Mode,
    PeerId SenderPeerId,
    byte[] Nonce,
    SessionId SessionId,
    byte[] EphemeralPublicKey,
    byte[] Proof);

public static class AuthPayloadCodec
{
    public const int ChallengePayloadSize = 133;
    public const int ResponsePayloadSize = 165;

    private const int ModeOffset = 0;
    private const int ReservedOffset = 1;
    private const int PeerIdOffset = 4;
    private const int NonceOffset = 20;
    private const int SessionIdOffset = 52;
    private const int PublicKeyOffset = 68;
    private const int ProofOffset = 133;

    public static byte[] EncodeChallenge(AuthChallengePayload payload)
    {
        ValidateCommon(
            payload.Mode,
            payload.SenderPeerId,
            payload.Nonce,
            payload.SessionId,
            payload.EphemeralPublicKey);

        var bytes = new byte[ChallengePayloadSize];
        bytes[ModeOffset] = (byte)payload.Mode;
        payload.SenderPeerId.WriteBytes(bytes.AsSpan(PeerIdOffset, PeerId.Size));
        payload.Nonce.CopyTo(bytes, NonceOffset);
        payload.SessionId.WriteBytes(bytes.AsSpan(SessionIdOffset, SessionId.Size));
        payload.EphemeralPublicKey.CopyTo(bytes, PublicKeyOffset);
        return bytes;
    }

    public static AuthChallengePayload DecodeChallenge(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ChallengePayloadSize)
        {
            throw new FormatException(
                $"AUTH_CHALLENGE payload must be exactly {ChallengePayloadSize} bytes.");
        }

        ValidateReserved(bytes);

        var payload = new AuthChallengePayload(
            DecodeMode(bytes[ModeOffset]),
            PeerId.FromBytes(bytes.Slice(PeerIdOffset, PeerId.Size)),
            bytes.Slice(NonceOffset, PairingCrypto.NonceSize).ToArray(),
            SessionId.FromBytes(bytes.Slice(SessionIdOffset, SessionId.Size)),
            bytes.Slice(
                PublicKeyOffset,
                PairingCrypto.P256UncompressedPublicKeySize).ToArray());

        ValidateCommon(
            payload.Mode,
            payload.SenderPeerId,
            payload.Nonce,
            payload.SessionId,
            payload.EphemeralPublicKey);

        return payload;
    }

    public static byte[] EncodeResponse(AuthResponsePayload payload)
    {
        ValidateCommon(
            payload.Mode,
            payload.SenderPeerId,
            payload.Nonce,
            payload.SessionId,
            payload.EphemeralPublicKey);

        if (payload.Proof.Length != PairingCrypto.DerivedKeySize)
        {
            throw new ArgumentException(
                "AUTH_RESPONSE proof must be exactly 32 bytes.",
                nameof(payload));
        }

        var bytes = new byte[ResponsePayloadSize];
        bytes[ModeOffset] = (byte)payload.Mode;
        payload.SenderPeerId.WriteBytes(bytes.AsSpan(PeerIdOffset, PeerId.Size));
        payload.Nonce.CopyTo(bytes, NonceOffset);
        payload.SessionId.WriteBytes(bytes.AsSpan(SessionIdOffset, SessionId.Size));
        payload.EphemeralPublicKey.CopyTo(bytes, PublicKeyOffset);
        payload.Proof.CopyTo(bytes, ProofOffset);
        return bytes;
    }

    public static AuthResponsePayload DecodeResponse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ResponsePayloadSize)
        {
            throw new FormatException(
                $"AUTH_RESPONSE payload must be exactly {ResponsePayloadSize} bytes.");
        }

        ValidateReserved(bytes);

        var payload = new AuthResponsePayload(
            DecodeMode(bytes[ModeOffset]),
            PeerId.FromBytes(bytes.Slice(PeerIdOffset, PeerId.Size)),
            bytes.Slice(NonceOffset, PairingCrypto.NonceSize).ToArray(),
            SessionId.FromBytes(bytes.Slice(SessionIdOffset, SessionId.Size)),
            bytes.Slice(
                PublicKeyOffset,
                PairingCrypto.P256UncompressedPublicKeySize).ToArray(),
            bytes.Slice(ProofOffset, PairingCrypto.DerivedKeySize).ToArray());

        ValidateCommon(
            payload.Mode,
            payload.SenderPeerId,
            payload.Nonce,
            payload.SessionId,
            payload.EphemeralPublicKey);

        return payload;
    }

    private static void ValidateCommon(
        AuthenticationMode mode,
        PeerId senderPeerId,
        byte[] nonce,
        SessionId sessionId,
        byte[] ephemeralPublicKey)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException("Unknown authentication mode.");
        }

        if (senderPeerId == PeerId.Zero)
        {
            throw new ArgumentException("Sender Peer ID must not be zero.");
        }

        if (nonce.Length != PairingCrypto.NonceSize)
        {
            throw new ArgumentException("Authentication nonce must be exactly 32 bytes.");
        }

        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException("Session ID must not be zero.");
        }

        if (ephemeralPublicKey.Length != PairingCrypto.P256UncompressedPublicKeySize)
        {
            throw new ArgumentException(
                "Authentication public-key field must be exactly 65 bytes.");
        }

        if (mode == AuthenticationMode.FirstPairing)
        {
            if (ephemeralPublicKey[0] != 0x04)
            {
                throw new ArgumentException(
                    "First-pairing public key must use uncompressed SEC1 encoding.");
            }
        }
        else if (ephemeralPublicKey.Any(static value => value != 0))
        {
            throw new ArgumentException(
                "Trusted reconnect must zero the ephemeral public-key field.");
        }
    }

    private static AuthenticationMode DecodeMode(byte value)
    {
        var mode = (AuthenticationMode)value;
        if (!Enum.IsDefined(mode))
        {
            throw new FormatException("Unknown authentication mode.");
        }

        return mode;
    }

    private static void ValidateReserved(ReadOnlySpan<byte> bytes)
    {
        if (bytes[ReservedOffset] != 0 ||
            bytes[ReservedOffset + 1] != 0 ||
            bytes[ReservedOffset + 2] != 0)
        {
            throw new FormatException(
                "Authentication reserved bytes must be zero.");
        }
    }
}
