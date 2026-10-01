using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Natsx.Controller.Protocol;

public static class PairingCrypto
{
    public const int NonceSize = 32;
    public const int P256UncompressedPublicKeySize = 65;
    public const int DerivedKeySize = 32;

    private static readonly byte[] PairingContext =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-V1");
    private static readonly byte[] PairingKeyInfo =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-KEY-V1");
    private static readonly byte[] SasContext =
        Encoding.ASCII.GetBytes("NATSX-SAS-V1");
    private static readonly byte[] PairingResponseContext =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-RESPONSE-V1");
    private static readonly byte[] TrustSecretInfo =
        Encoding.ASCII.GetBytes("NATSX-TRUST-SECRET-V1");
    private static readonly byte[] AndroidConfirmContext =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-CONFIRM-ANDROID-V1");
    private static readonly byte[] WindowsConfirmContext =
        Encoding.ASCII.GetBytes("NATSX-PAIRING-CONFIRM-WINDOWS-V1");

    public static byte[] ComputePairingTranscriptHash(
        PeerId androidPeerId,
        PeerId windowsPeerId,
        ReadOnlySpan<byte> androidNonce,
        ReadOnlySpan<byte> windowsNonce,
        ReadOnlySpan<byte> androidPublicKey,
        ReadOnlySpan<byte> windowsPublicKey)
    {
        ValidateNonce(androidNonce, nameof(androidNonce));
        ValidateNonce(windowsNonce, nameof(windowsNonce));
        ValidatePublicKey(androidPublicKey, nameof(androidPublicKey));
        ValidatePublicKey(windowsPublicKey, nameof(windowsPublicKey));

        byte[] transcript = new byte[
            PairingContext.Length +
            (PeerId.Size * 2) +
            (NonceSize * 2) +
            (P256UncompressedPublicKeySize * 2)];

        int offset = 0;
        Copy(PairingContext, transcript, ref offset);
        WritePeerId(androidPeerId, transcript, ref offset);
        WritePeerId(windowsPeerId, transcript, ref offset);
        Copy(androidNonce, transcript, ref offset);
        Copy(windowsNonce, transcript, ref offset);
        Copy(androidPublicKey, transcript, ref offset);
        Copy(windowsPublicKey, transcript, ref offset);

        try
        {
            return SHA256.HashData(transcript);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transcript);
        }
    }

    public static byte[] DerivePairingKey(
        ReadOnlySpan<byte> ecdhSharedSecret,
        ReadOnlySpan<byte> pairingTranscriptHash)
    {
        ValidateHash(pairingTranscriptHash, nameof(pairingTranscriptHash));
        return HkdfSha256Derive32(
            ecdhSharedSecret,
            pairingTranscriptHash,
            PairingKeyInfo);
    }

    public static string DeriveSixDigitSas(
        ReadOnlySpan<byte> pairingKey,
        ReadOnlySpan<byte> pairingTranscriptHash)
    {
        ValidateKey(pairingKey, nameof(pairingKey));
        ValidateHash(pairingTranscriptHash, nameof(pairingTranscriptHash));

        byte[] input = new byte[SasContext.Length + DerivedKeySize];
        SasContext.CopyTo(input, 0);
        pairingTranscriptHash.CopyTo(input.AsSpan(SasContext.Length));

        byte[] mac = HmacSha256(pairingKey, input);

        try
        {
            uint value = BinaryPrimitives.ReadUInt32BigEndian(mac);
            return (value % 1_000_000).ToString("D6");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(mac);
        }
    }


    public static byte[] ComputePairingResponseProof(
        ReadOnlySpan<byte> pairingKey,
        ReadOnlySpan<byte> pairingTranscriptHash,
        SessionId sessionId)
    {
        ValidateKey(pairingKey, nameof(pairingKey));
        ValidateHash(pairingTranscriptHash, nameof(pairingTranscriptHash));

        byte[] input = new byte[
            PairingResponseContext.Length +
            DerivedKeySize +
            SessionId.Size];

        int offset = 0;
        Copy(PairingResponseContext, input, ref offset);
        Copy(pairingTranscriptHash, input, ref offset);
        sessionId.WriteBytes(input.AsSpan(offset, SessionId.Size));

        try
        {
            return HmacSha256(pairingKey, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public static byte[] ComputePairingConfirmationProof(
        PairingConfirmationRole role,
        ReadOnlySpan<byte> pairingKey,
        ReadOnlySpan<byte> pairingTranscriptHash,
        SessionId sessionId)
    {
        ValidateKey(pairingKey, nameof(pairingKey));
        ValidateHash(pairingTranscriptHash, nameof(pairingTranscriptHash));

        ReadOnlySpan<byte> context = role switch
        {
            PairingConfirmationRole.Android => AndroidConfirmContext,
            PairingConfirmationRole.Windows => WindowsConfirmContext,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        byte[] input = new byte[
            context.Length +
            DerivedKeySize +
            SessionId.Size];

        int offset = 0;
        Copy(context, input, ref offset);
        Copy(pairingTranscriptHash, input, ref offset);
        sessionId.WriteBytes(input.AsSpan(offset, SessionId.Size));

        try
        {
            return HmacSha256(pairingKey, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public static bool VerifyPairingConfirmationProof(
        PairingConfirmationRole role,
        ReadOnlySpan<byte> pairingKey,
        ReadOnlySpan<byte> pairingTranscriptHash,
        SessionId sessionId,
        ReadOnlySpan<byte> suppliedProof)
    {
        if (suppliedProof.Length != DerivedKeySize)
        {
            return false;
        }

        byte[] expected = ComputePairingConfirmationProof(
            role,
            pairingKey,
            pairingTranscriptHash,
            sessionId);

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                expected,
                suppliedProof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public static byte[] DeriveTrustSecret(
        ReadOnlySpan<byte> pairingKey,
        ReadOnlySpan<byte> pairingTranscriptHash)
    {
        ValidateKey(pairingKey, nameof(pairingKey));
        ValidateHash(pairingTranscriptHash, nameof(pairingTranscriptHash));

        return HkdfSha256Derive32(
            pairingKey,
            pairingTranscriptHash,
            TrustSecretInfo);
    }

    private static byte[] HkdfSha256Derive32(
        ReadOnlySpan<byte> ikm,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        byte[] prk = HmacSha256(salt, ikm);
        byte[] expandInput = new byte[info.Length + 1];
        info.CopyTo(expandInput);
        expandInput[^1] = 0x01;

        try
        {
            return HmacSha256(prk, expandInput);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(expandInput);
        }
    }

    private static byte[] HmacSha256(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> data)
    {
        using var hmac = new HMACSHA256(key.ToArray());
        return hmac.ComputeHash(data.ToArray());
    }

    private static void ValidateNonce(
        ReadOnlySpan<byte> nonce,
        string parameterName)
    {
        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException(
                $"Nonce must be exactly {NonceSize} bytes.",
                parameterName);
        }
    }

    private static void ValidatePublicKey(
        ReadOnlySpan<byte> publicKey,
        string parameterName)
    {
        if (publicKey.Length != P256UncompressedPublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new ArgumentException(
                "P-256 public key must use 65-byte uncompressed SEC1 encoding.",
                parameterName);
        }
    }

    private static void ValidateHash(
        ReadOnlySpan<byte> hash,
        string parameterName)
    {
        if (hash.Length != DerivedKeySize)
        {
            throw new ArgumentException(
                $"SHA-256 hash must be exactly {DerivedKeySize} bytes.",
                parameterName);
        }
    }

    private static void ValidateKey(
        ReadOnlySpan<byte> key,
        string parameterName)
    {
        if (key.Length != DerivedKeySize)
        {
            throw new ArgumentException(
                $"Key must be exactly {DerivedKeySize} bytes.",
                parameterName);
        }
    }

    private static void WritePeerId(
        PeerId peerId,
        byte[] destination,
        ref int offset)
    {
        peerId.WriteBytes(destination.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;
    }

    private static void Copy(
        ReadOnlySpan<byte> source,
        byte[] destination,
        ref int offset)
    {
        source.CopyTo(destination.AsSpan(offset, source.Length));
        offset += source.Length;
    }
}


public enum PairingConfirmationRole
{
    Android,
    Windows,
}
