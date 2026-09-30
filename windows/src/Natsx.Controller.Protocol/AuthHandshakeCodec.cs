using System.Security.Cryptography;
using System.Text;

namespace Natsx.Controller.Protocol;

public readonly record struct AuthChallengePayload(
    PeerId ChallengerPeerId,
    PeerId TargetPeerId,
    byte[] ChallengeNonce);

public static class AuthChallengePayloadCodec
{
    public const int ChallengeSize = 32;
    public const int PayloadSize = PeerId.Size + PeerId.Size + ChallengeSize;

    public static byte[] Encode(AuthChallengePayload payload)
    {
        if (payload.ChallengeNonce.Length != ChallengeSize)
        {
            throw new ArgumentException(
                $"Authentication challenge must be exactly {ChallengeSize} bytes.",
                nameof(payload));
        }

        var bytes = new byte[PayloadSize];
        payload.ChallengerPeerId.WriteBytes(bytes.AsSpan(0, PeerId.Size));
        payload.TargetPeerId.WriteBytes(bytes.AsSpan(PeerId.Size, PeerId.Size));
        payload.ChallengeNonce.CopyTo(bytes, PeerId.Size * 2);
        return bytes;
    }

    public static AuthChallengePayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"AUTH_CHALLENGE payload must be exactly {PayloadSize} bytes.");
        }

        return new AuthChallengePayload(
            PeerId.FromBytes(bytes[..PeerId.Size]),
            PeerId.FromBytes(bytes.Slice(PeerId.Size, PeerId.Size)),
            bytes[(PeerId.Size * 2)..].ToArray());
    }

    public static byte[] CreateChallenge()
    {
        return RandomNumberGenerator.GetBytes(ChallengeSize);
    }
}

public readonly record struct AuthResponsePayload(
    PeerId ResponderPeerId,
    PeerId ChallengerPeerId,
    byte[] Proof);

public static class AuthResponsePayloadCodec
{
    public const int ProofSize = 32;
    public const int PayloadSize = PeerId.Size + PeerId.Size + ProofSize;

    public static byte[] Encode(AuthResponsePayload payload)
    {
        if (payload.Proof.Length != ProofSize)
        {
            throw new ArgumentException(
                $"Authentication proof must be exactly {ProofSize} bytes.",
                nameof(payload));
        }

        var bytes = new byte[PayloadSize];
        payload.ResponderPeerId.WriteBytes(bytes.AsSpan(0, PeerId.Size));
        payload.ChallengerPeerId.WriteBytes(bytes.AsSpan(PeerId.Size, PeerId.Size));
        payload.Proof.CopyTo(bytes, PeerId.Size * 2);
        return bytes;
    }

    public static AuthResponsePayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException($"AUTH_RESPONSE payload must be exactly {PayloadSize} bytes.");
        }

        return new AuthResponsePayload(
            PeerId.FromBytes(bytes[..PeerId.Size]),
            PeerId.FromBytes(bytes.Slice(PeerId.Size, PeerId.Size)),
            bytes[(PeerId.Size * 2)..].ToArray());
    }
}

public static class TrustedReconnectCrypto
{
    public const int TrustKeySize = 32;
    public const int SessionKeySize = 32;

    private static ReadOnlySpan<byte> ProofLabel => "NATSX-AUTH-V1"u8;
    private static ReadOnlySpan<byte> SessionLabel => "NATSX-SESSION-V1"u8;

    public static byte[] CreateProof(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        ReadOnlySpan<byte> challengeNonce,
        PeerId challengerPeerId,
        PeerId responderPeerId)
    {
        ValidateInputs(trustKey, challengeNonce);

        byte[] transcript = BuildProofTranscript(
            sessionId,
            challengeNonce,
            challengerPeerId,
            responderPeerId);

        try
        {
            return HMACSHA256.HashData(trustKey, transcript);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transcript);
        }
    }

    public static bool VerifyProof(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        ReadOnlySpan<byte> challengeNonce,
        PeerId challengerPeerId,
        PeerId responderPeerId,
        ReadOnlySpan<byte> suppliedProof)
    {
        if (suppliedProof.Length != AuthResponsePayloadCodec.ProofSize)
        {
            return false;
        }

        byte[] expected = CreateProof(
            trustKey,
            sessionId,
            challengeNonce,
            challengerPeerId,
            responderPeerId);

        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, suppliedProof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public static byte[] DeriveSessionKey(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        ReadOnlySpan<byte> challengeNonce,
        PeerId challengerPeerId,
        PeerId responderPeerId)
    {
        ValidateInputs(trustKey, challengeNonce);

        Span<byte> sessionBytes = stackalloc byte[SessionId.Size];
        Span<byte> challengerBytes = stackalloc byte[PeerId.Size];
        Span<byte> responderBytes = stackalloc byte[PeerId.Size];

        sessionId.WriteBytes(sessionBytes);
        challengerPeerId.WriteBytes(challengerBytes);
        responderPeerId.WriteBytes(responderBytes);

        byte[] salt = new byte[challengeNonce.Length + SessionId.Size];
        challengeNonce.CopyTo(salt);
        sessionBytes.CopyTo(salt.AsSpan(challengeNonce.Length));

        byte[] info = new byte[SessionLabel.Length + PeerId.Size + PeerId.Size];
        SessionLabel.CopyTo(info);
        challengerBytes.CopyTo(info.AsSpan(SessionLabel.Length));
        responderBytes.CopyTo(info.AsSpan(SessionLabel.Length + PeerId.Size));

        byte[] prk = HMACSHA256.HashData(salt, trustKey);

        try
        {
            byte[] expandInput = new byte[info.Length + 1];
            info.CopyTo(expandInput, 0);
            expandInput[^1] = 0x01;

            try
            {
                return HMACSHA256.HashData(prk, expandInput);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expandInput);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(info);
        }
    }

    private static byte[] BuildProofTranscript(
        SessionId sessionId,
        ReadOnlySpan<byte> challengeNonce,
        PeerId challengerPeerId,
        PeerId responderPeerId)
    {
        Span<byte> sessionBytes = stackalloc byte[SessionId.Size];
        Span<byte> challengerBytes = stackalloc byte[PeerId.Size];
        Span<byte> responderBytes = stackalloc byte[PeerId.Size];

        sessionId.WriteBytes(sessionBytes);
        challengerPeerId.WriteBytes(challengerBytes);
        responderPeerId.WriteBytes(responderBytes);

        byte[] transcript = new byte[
            ProofLabel.Length +
            SessionId.Size +
            AuthChallengePayloadCodec.ChallengeSize +
            PeerId.Size +
            PeerId.Size];

        int offset = 0;
        ProofLabel.CopyTo(transcript);
        offset += ProofLabel.Length;

        sessionBytes.CopyTo(transcript.AsSpan(offset));
        offset += SessionId.Size;

        challengeNonce.CopyTo(transcript.AsSpan(offset));
        offset += AuthChallengePayloadCodec.ChallengeSize;

        challengerBytes.CopyTo(transcript.AsSpan(offset));
        offset += PeerId.Size;

        responderBytes.CopyTo(transcript.AsSpan(offset));

        return transcript;
    }

    private static void ValidateInputs(
        ReadOnlySpan<byte> trustKey,
        ReadOnlySpan<byte> challengeNonce)
    {
        if (trustKey.Length != TrustKeySize)
        {
            throw new ArgumentException($"Trust key must be exactly {TrustKeySize} bytes.", nameof(trustKey));
        }

        if (challengeNonce.Length != AuthChallengePayloadCodec.ChallengeSize)
        {
            throw new ArgumentException(
                $"Challenge nonce must be exactly {AuthChallengePayloadCodec.ChallengeSize} bytes.",
                nameof(challengeNonce));
        }
    }
}
