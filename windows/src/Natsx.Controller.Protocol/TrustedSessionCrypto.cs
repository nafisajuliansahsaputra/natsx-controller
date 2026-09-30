using System.Security.Cryptography;
using System.Text;

namespace Natsx.Controller.Protocol;

public static class TrustedSessionCrypto
{
    public const int PairingRootKeySize = 32;
    public const int ChallengeSize = 32;
    public const int SessionKeySize = 32;
    public const int ProofSize = 16;

    private static readonly byte[] SessionInfoPrefix =
        Encoding.ASCII.GetBytes("NATSX-CONTROLLER-V1");

    private static readonly byte[] AndroidProofPrefix =
        Encoding.ASCII.GetBytes("NATSX-ANDROID-PROOF-V1");

    public static byte[] DeriveSessionKey(
        ReadOnlySpan<byte> pairingRootKey,
        ReadOnlySpan<byte> challenge,
        SessionId androidDeviceId,
        SessionId windowsDeviceId,
        SessionId sessionId)
    {
        if (pairingRootKey.Length != PairingRootKeySize)
            throw new ArgumentException("Pairing root key must be 32 bytes.", nameof(pairingRootKey));

        if (challenge.Length != ChallengeSize)
            throw new ArgumentException("Authentication challenge must be 32 bytes.", nameof(challenge));

        Span<byte> androidBytes = stackalloc byte[16];
        Span<byte> windowsBytes = stackalloc byte[16];
        Span<byte> sessionBytes = stackalloc byte[16];

        androidDeviceId.WriteBytes(androidBytes);
        windowsDeviceId.WriteBytes(windowsBytes);
        sessionId.WriteBytes(sessionBytes);

        byte[] info = new byte[SessionInfoPrefix.Length + 48];
        SessionInfoPrefix.CopyTo(info, 0);
        androidBytes.CopyTo(info.AsSpan(SessionInfoPrefix.Length, 16));
        windowsBytes.CopyTo(info.AsSpan(SessionInfoPrefix.Length + 16, 16));
        sessionBytes.CopyTo(info.AsSpan(SessionInfoPrefix.Length + 32, 16));

        byte[] prk;
        using (var extract = new HMACSHA256(challenge.ToArray()))
            prk = extract.ComputeHash(pairingRootKey.ToArray());

        byte[] expandInput = new byte[info.Length + 1];
        info.CopyTo(expandInput, 0);
        expandInput[^1] = 0x01;

        byte[] okm;
        using (var expand = new HMACSHA256(prk))
            okm = expand.ComputeHash(expandInput);

        byte[] result = okm.AsSpan(0, SessionKeySize).ToArray();

        CryptographicOperations.ZeroMemory(prk);
        CryptographicOperations.ZeroMemory(okm);
        CryptographicOperations.ZeroMemory(info);
        CryptographicOperations.ZeroMemory(expandInput);

        return result;
    }

    public static byte[] ComputeAndroidProof(
        ReadOnlySpan<byte> sessionKey,
        ReadOnlySpan<byte> androidHelloPayload,
        ReadOnlySpan<byte> windowsHelloPayload,
        ReadOnlySpan<byte> challenge,
        SessionId sessionId)
    {
        if (sessionKey.Length != SessionKeySize)
            throw new ArgumentException("Session key must be 32 bytes.", nameof(sessionKey));

        if (challenge.Length != ChallengeSize)
            throw new ArgumentException("Authentication challenge must be 32 bytes.", nameof(challenge));

        byte[] transcript = new byte[androidHelloPayload.Length + windowsHelloPayload.Length];
        androidHelloPayload.CopyTo(transcript);
        windowsHelloPayload.CopyTo(transcript.AsSpan(androidHelloPayload.Length));

        byte[] transcriptHash = SHA256.HashData(transcript);

        Span<byte> sessionBytes = stackalloc byte[16];
        sessionId.WriteBytes(sessionBytes);

        byte[] proofInput = new byte[
            AndroidProofPrefix.Length +
            transcriptHash.Length +
            challenge.Length +
            sessionBytes.Length];

        int offset = 0;
        AndroidProofPrefix.CopyTo(proofInput, offset);
        offset += AndroidProofPrefix.Length;
        transcriptHash.CopyTo(proofInput, offset);
        offset += transcriptHash.Length;
        challenge.CopyTo(proofInput.AsSpan(offset, challenge.Length));
        offset += challenge.Length;
        sessionBytes.CopyTo(proofInput.AsSpan(offset, sessionBytes.Length));

        byte[] hash;
        using (var hmac = new HMACSHA256(sessionKey.ToArray()))
            hash = hmac.ComputeHash(proofInput);

        byte[] proof = hash.AsSpan(0, ProofSize).ToArray();

        CryptographicOperations.ZeroMemory(hash);
        CryptographicOperations.ZeroMemory(transcript);
        CryptographicOperations.ZeroMemory(transcriptHash);
        CryptographicOperations.ZeroMemory(proofInput);

        return proof;
    }

    public static bool VerifyAndroidProof(
        ReadOnlySpan<byte> expectedProof,
        ReadOnlySpan<byte> suppliedProof)
    {
        if (expectedProof.Length != ProofSize || suppliedProof.Length != ProofSize)
            return false;

        return CryptographicOperations.FixedTimeEquals(expectedProof, suppliedProof);
    }
}
