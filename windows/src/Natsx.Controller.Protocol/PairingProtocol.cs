using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public enum PairingRole : byte
{
    Initiator = 1,
    Responder = 2,
}

public readonly record struct PairingOfferPayload(
    PeerId InitiatorPeerId,
    byte[] InitiatorNonce,
    byte[] InitiatorPublicKey);

public readonly record struct PairingResponsePayload(
    PeerId ResponderPeerId,
    byte[] ResponderNonce,
    byte[] ResponderPublicKey);

public static class PairingExchangePayloadCodec
{
    public const int NonceSize = 32;
    public const int UncompressedP256PublicKeySize = 65;
    public const int PayloadSize =
        PeerId.Size + NonceSize + UncompressedP256PublicKeySize;

    public static byte[] EncodeOffer(PairingOfferPayload payload)
    {
        ValidateNonce(payload.InitiatorNonce);
        ValidatePublicKey(payload.InitiatorPublicKey);

        var bytes = new byte[PayloadSize];
        payload.InitiatorPeerId.WriteBytes(bytes.AsSpan(0, PeerId.Size));
        payload.InitiatorNonce.CopyTo(bytes, PeerId.Size);
        payload.InitiatorPublicKey.CopyTo(bytes, PeerId.Size + NonceSize);
        return bytes;
    }

    public static PairingOfferPayload DecodeOffer(ReadOnlySpan<byte> bytes)
    {
        ValidatePayloadLength(bytes, "PAIRING_OFFER");

        return new PairingOfferPayload(
            PeerId.FromBytes(bytes[..PeerId.Size]),
            bytes.Slice(PeerId.Size, NonceSize).ToArray(),
            ReadAndValidatePublicKey(
                bytes.Slice(PeerId.Size + NonceSize, UncompressedP256PublicKeySize)));
    }

    public static byte[] EncodeResponse(PairingResponsePayload payload)
    {
        ValidateNonce(payload.ResponderNonce);
        ValidatePublicKey(payload.ResponderPublicKey);

        var bytes = new byte[PayloadSize];
        payload.ResponderPeerId.WriteBytes(bytes.AsSpan(0, PeerId.Size));
        payload.ResponderNonce.CopyTo(bytes, PeerId.Size);
        payload.ResponderPublicKey.CopyTo(bytes, PeerId.Size + NonceSize);
        return bytes;
    }

    public static PairingResponsePayload DecodeResponse(ReadOnlySpan<byte> bytes)
    {
        ValidatePayloadLength(bytes, "PAIRING_RESPONSE");

        return new PairingResponsePayload(
            PeerId.FromBytes(bytes[..PeerId.Size]),
            bytes.Slice(PeerId.Size, NonceSize).ToArray(),
            ReadAndValidatePublicKey(
                bytes.Slice(PeerId.Size + NonceSize, UncompressedP256PublicKeySize)));
    }

    public static byte[] CreateNonce() =>
        RandomNumberGenerator.GetBytes(NonceSize);

    private static void ValidatePayloadLength(
        ReadOnlySpan<byte> bytes,
        string messageName)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"{messageName} payload must be exactly {PayloadSize} bytes.");
        }
    }

    private static void ValidateNonce(ReadOnlySpan<byte> nonce)
    {
        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException(
                $"Pairing nonce must be exactly {NonceSize} bytes.");
        }
    }

    private static void ValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != UncompressedP256PublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new ArgumentException(
                "Pairing public key must be a 65-byte uncompressed P-256 point.");
        }
    }

    private static byte[] ReadAndValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != UncompressedP256PublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new FormatException(
                "Pairing public key must be a 65-byte uncompressed P-256 point.");
        }

        return publicKey.ToArray();
    }
}

public readonly record struct PairingConfirmPayload(
    PeerId PeerId,
    PairingRole Role,
    byte[] Proof);

public static class PairingConfirmPayloadCodec
{
    public const int ProofSize = 32;
    public const int PayloadSize = PeerId.Size + 1 + ProofSize;

    public static byte[] Encode(PairingConfirmPayload payload)
    {
        ValidateRole(payload.Role);

        if (payload.Proof.Length != ProofSize)
        {
            throw new ArgumentException(
                $"Pairing confirmation proof must be exactly {ProofSize} bytes.");
        }

        var bytes = new byte[PayloadSize];
        payload.PeerId.WriteBytes(bytes.AsSpan(0, PeerId.Size));
        bytes[PeerId.Size] = (byte)payload.Role;
        payload.Proof.CopyTo(bytes, PeerId.Size + 1);
        return bytes;
    }

    public static PairingConfirmPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"PAIRING_CONFIRM payload must be exactly {PayloadSize} bytes.");
        }

        var role = (PairingRole)bytes[PeerId.Size];
        ValidateRole(role);

        return new PairingConfirmPayload(
            PeerId.FromBytes(bytes[..PeerId.Size]),
            role,
            bytes[(PeerId.Size + 1)..].ToArray());
    }

    private static void ValidateRole(PairingRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new FormatException("Unknown pairing role.");
        }
    }
}

public enum PairingAbortReason : byte
{
    UserRejected = 1,
    CodeMismatch = 2,
    Timeout = 3,
    ProtocolError = 4,
}

public readonly record struct PairingAbortPayload(PairingAbortReason Reason);

public static class PairingAbortPayloadCodec
{
    public const int PayloadSize = 4;

    public static byte[] Encode(PairingAbortPayload payload)
    {
        ValidateReason(payload.Reason);
        return [(byte)payload.Reason, 0, 0, 0];
    }

    public static PairingAbortPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PayloadSize)
        {
            throw new FormatException(
                $"PAIRING_ABORT payload must be exactly {PayloadSize} bytes.");
        }

        if (bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0)
        {
            throw new FormatException("PAIRING_ABORT reserved bytes must be zero.");
        }

        var reason = (PairingAbortReason)bytes[0];
        ValidateReason(reason);
        return new PairingAbortPayload(reason);
    }

    private static void ValidateReason(PairingAbortReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new FormatException("Unknown pairing abort reason.");
        }
    }
}

public sealed class PairingKeyAgreement : IDisposable
{
    private readonly ECDiffieHellman _ecdh;
    private bool _disposed;

    public PairingKeyAgreement()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        PublicKey = ExportUncompressedPublicKey(_ecdh);
    }

    public byte[] PublicKey { get; }

    public byte[] DeriveSharedSecret(ReadOnlySpan<byte> remotePublicKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (remotePublicKey.Length != PairingExchangePayloadCodec.UncompressedP256PublicKeySize ||
            remotePublicKey[0] != 0x04)
        {
            throw new ArgumentException(
                "Remote key must be a 65-byte uncompressed P-256 point.",
                nameof(remotePublicKey));
        }

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = remotePublicKey.Slice(1, 32).ToArray(),
                Y = remotePublicKey.Slice(33, 32).ToArray(),
            },
        };

        using ECDiffieHellman remote = ECDiffieHellman.Create(parameters);
        return _ecdh.DeriveRawSecretAgreement(remote.PublicKey);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ecdh.Dispose();
        CryptographicOperations.ZeroMemory(PublicKey);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static byte[] ExportUncompressedPublicKey(ECDiffieHellman ecdh)
    {
        ECParameters parameters = ecdh.ExportParameters(false);

        if (parameters.Q.X is null ||
            parameters.Q.Y is null ||
            parameters.Q.X.Length != 32 ||
            parameters.Q.Y.Length != 32)
        {
            throw new CryptographicException("Unexpected P-256 public key shape.");
        }

        var output = new byte[65];
        output[0] = 0x04;
        parameters.Q.X.CopyTo(output, 1);
        parameters.Q.Y.CopyTo(output, 33);
        return output;
    }
}

public static class PairingCrypto
{
    public const int SharedSecretSize = 32;
    public const int TrustKeySize = 32;

    private static ReadOnlySpan<byte> TranscriptLabel => "NATSX-PAIRING-V1"u8;
    private static ReadOnlySpan<byte> TrustInfo => "NATSX-PAIRING-TRUST-V1"u8;
    private static ReadOnlySpan<byte> SasInfo => "NATSX-PAIRING-SAS-V1"u8;
    private static ReadOnlySpan<byte> ConfirmLabel => "NATSX-PAIRING-CONFIRM-V1"u8;

    public static byte[] ComputeTranscriptHash(
        PairingOfferPayload offer,
        PairingResponsePayload response)
    {
        byte[] offerBytes = PairingExchangePayloadCodec.EncodeOffer(offer);
        byte[] responseBytes = PairingExchangePayloadCodec.EncodeResponse(response);
        byte[] transcript = new byte[
            TranscriptLabel.Length + offerBytes.Length + responseBytes.Length];

        int offset = 0;
        TranscriptLabel.CopyTo(transcript);
        offset += TranscriptLabel.Length;
        offerBytes.CopyTo(transcript, offset);
        offset += offerBytes.Length;
        responseBytes.CopyTo(transcript, offset);

        byte[] hash = SHA256.HashData(transcript);
        CryptographicOperations.ZeroMemory(transcript);
        return hash;
    }

    public static byte[] DeriveTrustKey(
        ReadOnlySpan<byte> sharedSecret,
        ReadOnlySpan<byte> transcriptHash)
    {
        ValidateKdfInputs(sharedSecret, transcriptHash);
        return HkdfSha256(sharedSecret, transcriptHash, TrustInfo);
    }

    public static string DeriveSixDigitCode(
        ReadOnlySpan<byte> sharedSecret,
        ReadOnlySpan<byte> transcriptHash)
    {
        ValidateKdfInputs(sharedSecret, transcriptHash);

        byte[] sasKey = HkdfSha256(sharedSecret, transcriptHash, SasInfo);
        try
        {
            uint value = BinaryPrimitives.ReadUInt32BigEndian(sasKey);
            return (value % 1_000_000).ToString("D6");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sasKey);
        }
    }

    public static byte[] CreateConfirmationProof(
        ReadOnlySpan<byte> trustKey,
        ReadOnlySpan<byte> transcriptHash,
        PairingRole role)
    {
        if (trustKey.Length != TrustKeySize)
        {
            throw new ArgumentException(
                $"Trust key must be exactly {TrustKeySize} bytes.",
                nameof(trustKey));
        }

        if (transcriptHash.Length != 32)
        {
            throw new ArgumentException(
                "Pairing transcript hash must be exactly 32 bytes.",
                nameof(transcriptHash));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        byte[] input = new byte[ConfirmLabel.Length + 32 + 1];
        ConfirmLabel.CopyTo(input);
        transcriptHash.CopyTo(input.AsSpan(ConfirmLabel.Length));
        input[^1] = (byte)role;

        try
        {
            return HMACSHA256.HashData(trustKey, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public static bool VerifyConfirmationProof(
        ReadOnlySpan<byte> trustKey,
        ReadOnlySpan<byte> transcriptHash,
        PairingRole role,
        ReadOnlySpan<byte> suppliedProof)
    {
        if (suppliedProof.Length != PairingConfirmPayloadCodec.ProofSize)
        {
            return false;
        }

        byte[] expected =
            CreateConfirmationProof(trustKey, transcriptHash, role);

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

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> ikm,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        byte[] prk = HMACSHA256.HashData(salt, ikm);

        try
        {
            byte[] expandInput = new byte[info.Length + 1];
            info.CopyTo(expandInput);
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
        }
    }

    private static void ValidateKdfInputs(
        ReadOnlySpan<byte> sharedSecret,
        ReadOnlySpan<byte> transcriptHash)
    {
        if (sharedSecret.Length != SharedSecretSize)
        {
            throw new ArgumentException(
                $"P-256 shared secret must be exactly {SharedSecretSize} bytes.",
                nameof(sharedSecret));
        }

        if (transcriptHash.Length != 32)
        {
            throw new ArgumentException(
                "Pairing transcript hash must be exactly 32 bytes.",
                nameof(transcriptHash));
        }
    }
}
