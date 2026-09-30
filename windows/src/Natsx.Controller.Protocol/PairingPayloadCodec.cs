using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public readonly record struct PairRequestPayload(
    PeerId ControllerPeerId,
    byte[] ClientNonce,
    long ExpiresUnixSeconds,
    byte[] Proof);

public readonly record struct PairResponsePayload(
    byte[] ServerNonce,
    byte[] Proof);

public readonly record struct AuthChallengePayload(
    PeerId ControllerPeerId,
    byte[] ClientNonce,
    byte[] Proof);

public readonly record struct AuthResponsePayload(
    byte[] ServerNonce,
    byte[] Proof);

public readonly record struct SessionReadyPayload(byte[] Proof);

public static class PairingPayloadCodec
{
    public const int PairRequestSize =
        PeerId.Size + PairingCrypto.NonceSize + sizeof(long) + PairingCrypto.ProofSize;

    public const int PairResponseSize =
        PairingCrypto.NonceSize + PairingCrypto.ProofSize;

    public const int AuthChallengeSize =
        PeerId.Size + PairingCrypto.NonceSize + PairingCrypto.ProofSize;

    public const int AuthResponseSize =
        PairingCrypto.NonceSize + PairingCrypto.ProofSize;

    public const int SessionReadySize = PairingCrypto.ProofSize;

    public static byte[] EncodePairRequest(PairRequestPayload payload)
    {
        ValidateNonce(payload.ClientNonce, nameof(payload));
        ValidateProof(payload.Proof, nameof(payload));

        var output = new byte[PairRequestSize];
        payload.ControllerPeerId.WriteBytes(output.AsSpan(0, PeerId.Size));
        payload.ClientNonce.CopyTo(output, PeerId.Size);
        BinaryPrimitives.WriteInt64LittleEndian(
            output.AsSpan(PeerId.Size + PairingCrypto.NonceSize, sizeof(long)),
            payload.ExpiresUnixSeconds);
        payload.Proof.CopyTo(
            output,
            PeerId.Size + PairingCrypto.NonceSize + sizeof(long));
        return output;
    }

    public static PairRequestPayload DecodePairRequest(ReadOnlySpan<byte> payload)
    {
        RequireSize(payload, PairRequestSize, nameof(PairRequestPayload));

        return new PairRequestPayload(
            PeerId.FromBytes(payload[..PeerId.Size]),
            payload.Slice(PeerId.Size, PairingCrypto.NonceSize).ToArray(),
            BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(PeerId.Size + PairingCrypto.NonceSize, sizeof(long))),
            payload[^PairingCrypto.ProofSize..].ToArray());
    }

    public static byte[] EncodePairResponse(PairResponsePayload payload)
    {
        ValidateNonce(payload.ServerNonce, nameof(payload));
        ValidateProof(payload.Proof, nameof(payload));

        var output = new byte[PairResponseSize];
        payload.ServerNonce.CopyTo(output, 0);
        payload.Proof.CopyTo(output, PairingCrypto.NonceSize);
        return output;
    }

    public static PairResponsePayload DecodePairResponse(ReadOnlySpan<byte> payload)
    {
        RequireSize(payload, PairResponseSize, nameof(PairResponsePayload));

        return new PairResponsePayload(
            payload[..PairingCrypto.NonceSize].ToArray(),
            payload[PairingCrypto.NonceSize..].ToArray());
    }

    public static byte[] EncodeAuthChallenge(AuthChallengePayload payload)
    {
        ValidateNonce(payload.ClientNonce, nameof(payload));
        ValidateProof(payload.Proof, nameof(payload));

        var output = new byte[AuthChallengeSize];
        payload.ControllerPeerId.WriteBytes(output.AsSpan(0, PeerId.Size));
        payload.ClientNonce.CopyTo(output, PeerId.Size);
        payload.Proof.CopyTo(output, PeerId.Size + PairingCrypto.NonceSize);
        return output;
    }

    public static AuthChallengePayload DecodeAuthChallenge(ReadOnlySpan<byte> payload)
    {
        RequireSize(payload, AuthChallengeSize, nameof(AuthChallengePayload));

        return new AuthChallengePayload(
            PeerId.FromBytes(payload[..PeerId.Size]),
            payload.Slice(PeerId.Size, PairingCrypto.NonceSize).ToArray(),
            payload[^PairingCrypto.ProofSize..].ToArray());
    }

    public static byte[] EncodeAuthResponse(AuthResponsePayload payload)
    {
        ValidateNonce(payload.ServerNonce, nameof(payload));
        ValidateProof(payload.Proof, nameof(payload));

        var output = new byte[AuthResponseSize];
        payload.ServerNonce.CopyTo(output, 0);
        payload.Proof.CopyTo(output, PairingCrypto.NonceSize);
        return output;
    }

    public static AuthResponsePayload DecodeAuthResponse(ReadOnlySpan<byte> payload)
    {
        RequireSize(payload, AuthResponseSize, nameof(AuthResponsePayload));

        return new AuthResponsePayload(
            payload[..PairingCrypto.NonceSize].ToArray(),
            payload[PairingCrypto.NonceSize..].ToArray());
    }

    public static byte[] EncodeSessionReady(SessionReadyPayload payload)
    {
        ValidateProof(payload.Proof, nameof(payload));
        return payload.Proof.ToArray();
    }

    public static SessionReadyPayload DecodeSessionReady(ReadOnlySpan<byte> payload)
    {
        RequireSize(payload, SessionReadySize, nameof(SessionReadyPayload));
        return new SessionReadyPayload(payload.ToArray());
    }

    private static void ValidateNonce(byte[] value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (value.Length != PairingCrypto.NonceSize)
        {
            throw new ArgumentException(
                $"Nonce must be exactly {PairingCrypto.NonceSize} bytes.",
                paramName);
        }
    }

    private static void ValidateProof(byte[] value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (value.Length != PairingCrypto.ProofSize)
        {
            throw new ArgumentException(
                $"Proof must be exactly {PairingCrypto.ProofSize} bytes.",
                paramName);
        }
    }

    private static void RequireSize(
        ReadOnlySpan<byte> payload,
        int expected,
        string payloadName)
    {
        if (payload.Length != expected)
        {
            throw new FormatException(
                $"{payloadName} payload must be exactly {expected} bytes.");
        }
    }
}
