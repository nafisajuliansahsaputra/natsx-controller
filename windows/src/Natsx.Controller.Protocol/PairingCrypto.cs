using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Natsx.Controller.Protocol;

public static class PairingCrypto
{
    public const int PairingRootKeySize = 32;
    public const int ConfirmationTagSize = 16;

    private static readonly byte[] RootInfo =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-ROOT-V1");

    private static readonly byte[] SasPrefix =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-SAS-V1");

    private static readonly byte[] AndroidConfirmPrefix =
        Encoding.ASCII.GetBytes("NATSX-ANDROID-CONFIRM-V1");

    private static readonly byte[] WindowsConfirmPrefix =
        Encoding.ASCII.GetBytes("NATSX-WINDOWS-CONFIRM-V1");

    private static readonly byte[] CompletePrefix =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-COMPLETE-V1");

    public static ECDiffieHellman CreateEphemeralKey() =>
        ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] ExportPublicKey(ECDiffieHellman key) =>
        key.PublicKey.ExportSubjectPublicKeyInfo();

    public static byte[] ComputeTranscriptHash(
        ReadOnlySpan<byte> pairRequest,
        ReadOnlySpan<byte> pairResponse)
    {
        byte[] transcript = new byte[
            pairRequest.Length + pairResponse.Length];

        pairRequest.CopyTo(transcript);
        pairResponse.CopyTo(transcript.AsSpan(pairRequest.Length));

        byte[] hash = SHA256.HashData(transcript);
        CryptographicOperations.ZeroMemory(transcript);
        return hash;
    }

    public static byte[] DerivePairingRootKey(
        ECDiffieHellman localEphemeralKey,
        ReadOnlySpan<byte> peerPublicKeyDer,
        ReadOnlySpan<byte> transcriptHash)
    {
        if (transcriptHash.Length != 32)
            throw new ArgumentException("Pairing transcript hash must be 32 bytes.", nameof(transcriptHash));

        using ECDiffieHellman peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKeyDer, out int bytesRead);

        if (bytesRead != peerPublicKeyDer.Length)
            throw new CryptographicException("Pairing peer public key contains trailing bytes.");

        byte[] sharedSecret =
            localEphemeralKey.DeriveRawSecretAgreement(peer.PublicKey);

        try
        {
            return HkdfSha256(
                sharedSecret,
                transcriptHash,
                RootInfo,
                PairingRootKeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public static string ComputeSas(
        ReadOnlySpan<byte> pairingRootKey,
        ReadOnlySpan<byte> transcriptHash)
    {
        byte[] hash = ComputeHmac(
            pairingRootKey,
            SasPrefix,
            transcriptHash);

        uint value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(0, 4));
        CryptographicOperations.ZeroMemory(hash);

        return (value % 1_000_000).ToString("D6");
    }

    public static byte[] ComputeConfirmationTag(
        PairingRole role,
        ReadOnlySpan<byte> pairingRootKey,
        ReadOnlySpan<byte> transcriptHash)
    {
        byte[] prefix = role switch
        {
            PairingRole.Android => AndroidConfirmPrefix,
            PairingRole.Windows => WindowsConfirmPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        byte[] hash = ComputeHmac(
            pairingRootKey,
            prefix,
            transcriptHash);

        byte[] tag = hash.AsSpan(0, ConfirmationTagSize).ToArray();
        CryptographicOperations.ZeroMemory(hash);
        return tag;
    }

    public static byte[] ComputeCompletionTag(
        ReadOnlySpan<byte> pairingRootKey,
        ReadOnlySpan<byte> transcriptHash)
    {
        byte[] hash = ComputeHmac(
            pairingRootKey,
            CompletePrefix,
            transcriptHash);

        byte[] tag = hash.AsSpan(0, ConfirmationTagSize).ToArray();
        CryptographicOperations.ZeroMemory(hash);
        return tag;
    }

    public static bool VerifyTag(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> supplied)
    {
        return expected.Length == ConfirmationTagSize &&
               supplied.Length == ConfirmationTagSize &&
               CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static byte[] ComputeHmac(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> transcriptHash)
    {
        if (key.Length != PairingRootKeySize)
            throw new ArgumentException("Pairing root key must be 32 bytes.", nameof(key));

        if (transcriptHash.Length != 32)
            throw new ArgumentException("Pairing transcript hash must be 32 bytes.", nameof(transcriptHash));

        byte[] input = new byte[prefix.Length + transcriptHash.Length];
        prefix.CopyTo(input);
        transcriptHash.CopyTo(input.AsSpan(prefix.Length));

        using var hmac = new HMACSHA256(key.ToArray());
        byte[] hash = hmac.ComputeHash(input);
        CryptographicOperations.ZeroMemory(input);
        return hash;
    }

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> ikm,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info,
        int length)
    {
        if (length is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(length));

        byte[] prk;

        using (var extract = new HMACSHA256(salt.ToArray()))
            prk = extract.ComputeHash(ikm.ToArray());

        byte[] expandInput = new byte[info.Length + 1];
        info.CopyTo(expandInput);
        expandInput[^1] = 0x01;

        byte[] block;

        using (var expand = new HMACSHA256(prk))
            block = expand.ComputeHash(expandInput);

        byte[] result = block.AsSpan(0, length).ToArray();

        CryptographicOperations.ZeroMemory(prk);
        CryptographicOperations.ZeroMemory(block);
        CryptographicOperations.ZeroMemory(expandInput);

        return result;
    }
}
