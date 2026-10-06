using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public enum PairingRole : byte
{
    AndroidController = 1,
    WindowsReceiver = 2,
}

public enum PairingAbortReason : byte
{
    UserRejected = 1,
    CodeMismatch = 2,
    Timeout = 3,
    ProtocolError = 4,
}

public readonly record struct PairingOfferPayload(
    PeerId AndroidPeerId,
    byte[] AndroidNonce,
    byte[] AndroidPublicKey);

public readonly record struct PairingResponsePayload(
    PeerId WindowsPeerId,
    byte[] WindowsNonce,
    byte[] WindowsPublicKey);

public readonly record struct PairingConfirmPayload(
    PeerId PeerId,
    PairingRole Role,
    byte[] Proof);

public readonly record struct PairingAbortPayload(
    PairingAbortReason Reason);

public static class PairingPayloadCodec
{
    public const int ExchangePayloadSize =
        PeerId.Size +
        PairingCrypto.NonceSize +
        PairingCrypto.P256UncompressedPublicKeySize;

    public const int ConfirmationProofSize =
        PairingCrypto.DerivedKeySize;

    public const int ConfirmationPayloadSize =
        PeerId.Size +
        1 +
        ConfirmationProofSize;

    public const int AbortPayloadSize = 4;

    public static byte[] CreateNonce() =>
        RandomNumberGenerator.GetBytes(
            PairingCrypto.NonceSize);

    public static byte[] EncodeOffer(
        PairingOfferPayload payload)
    {
        ValidateNonce(
            payload.AndroidNonce);
        ValidatePublicKey(
            payload.AndroidPublicKey);

        var bytes =
            new byte[ExchangePayloadSize];

        payload.AndroidPeerId.WriteBytes(
            bytes.AsSpan(
                0,
                PeerId.Size));

        payload.AndroidNonce.CopyTo(
            bytes,
            PeerId.Size);

        payload.AndroidPublicKey.CopyTo(
            bytes,
            PeerId.Size +
            PairingCrypto.NonceSize);

        return bytes;
    }

    public static PairingOfferPayload DecodeOffer(
        ReadOnlySpan<byte> bytes)
    {
        ValidateExchangeLength(
            bytes,
            "PAIRING_OFFER");

        byte[] nonce =
            bytes.Slice(
                    PeerId.Size,
                    PairingCrypto.NonceSize)
                .ToArray();

        byte[] publicKey =
            bytes.Slice(
                    PeerId.Size +
                    PairingCrypto.NonceSize,
                    PairingCrypto
                        .P256UncompressedPublicKeySize)
                .ToArray();

        try
        {
            ValidateNonce(nonce);
            ValidatePublicKey(publicKey);

            return new PairingOfferPayload(
                PeerId.FromBytes(
                    bytes[..PeerId.Size]),
                nonce,
                publicKey);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(
                nonce);
            CryptographicOperations.ZeroMemory(
                publicKey);
            throw;
        }
    }

    public static byte[] EncodeResponse(
        PairingResponsePayload payload)
    {
        ValidateNonce(
            payload.WindowsNonce);
        ValidatePublicKey(
            payload.WindowsPublicKey);

        var bytes =
            new byte[ExchangePayloadSize];

        payload.WindowsPeerId.WriteBytes(
            bytes.AsSpan(
                0,
                PeerId.Size));

        payload.WindowsNonce.CopyTo(
            bytes,
            PeerId.Size);

        payload.WindowsPublicKey.CopyTo(
            bytes,
            PeerId.Size +
            PairingCrypto.NonceSize);

        return bytes;
    }

    public static PairingResponsePayload DecodeResponse(
        ReadOnlySpan<byte> bytes)
    {
        ValidateExchangeLength(
            bytes,
            "PAIRING_RESPONSE");

        byte[] nonce =
            bytes.Slice(
                    PeerId.Size,
                    PairingCrypto.NonceSize)
                .ToArray();

        byte[] publicKey =
            bytes.Slice(
                    PeerId.Size +
                    PairingCrypto.NonceSize,
                    PairingCrypto
                        .P256UncompressedPublicKeySize)
                .ToArray();

        try
        {
            ValidateNonce(nonce);
            ValidatePublicKey(publicKey);

            return new PairingResponsePayload(
                PeerId.FromBytes(
                    bytes[..PeerId.Size]),
                nonce,
                publicKey);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(
                nonce);
            CryptographicOperations.ZeroMemory(
                publicKey);
            throw;
        }
    }

    public static byte[] EncodeConfirm(
        PairingConfirmPayload payload)
    {
        ValidateRole(
            payload.Role);

        if (payload.Proof.Length !=
            ConfirmationProofSize)
        {
            throw new ArgumentException(
                $"Pairing confirmation proof must be exactly {ConfirmationProofSize} bytes.",
                nameof(payload));
        }

        var bytes =
            new byte[ConfirmationPayloadSize];

        payload.PeerId.WriteBytes(
            bytes.AsSpan(
                0,
                PeerId.Size));

        bytes[PeerId.Size] =
            (byte)payload.Role;

        payload.Proof.CopyTo(
            bytes,
            PeerId.Size + 1);

        return bytes;
    }

    public static PairingConfirmPayload DecodeConfirm(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length !=
            ConfirmationPayloadSize)
        {
            throw new FormatException(
                $"PAIRING_CONFIRM payload must be exactly {ConfirmationPayloadSize} bytes.");
        }

        PairingRole role =
            (PairingRole)bytes[PeerId.Size];

        ValidateRole(
            role);

        return new PairingConfirmPayload(
            PeerId.FromBytes(
                bytes[..PeerId.Size]),
            role,
            bytes[(PeerId.Size + 1)..]
                .ToArray());
    }

    public static byte[] EncodeAbort(
        PairingAbortPayload payload)
    {
        ValidateAbortReason(
            payload.Reason);

        return
        [
            (byte)payload.Reason,
            0,
            0,
            0,
        ];
    }

    public static PairingAbortPayload DecodeAbort(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != AbortPayloadSize)
        {
            throw new FormatException(
                $"PAIRING_ABORT payload must be exactly {AbortPayloadSize} bytes.");
        }

        if (bytes[1] != 0 ||
            bytes[2] != 0 ||
            bytes[3] != 0)
        {
            throw new FormatException(
                "PAIRING_ABORT reserved bytes must be zero.");
        }

        PairingAbortReason reason =
            (PairingAbortReason)bytes[0];

        ValidateAbortReason(
            reason);

        return new PairingAbortPayload(
            reason);
    }

    private static void ValidateExchangeLength(
        ReadOnlySpan<byte> bytes,
        string messageName)
    {
        if (bytes.Length !=
            ExchangePayloadSize)
        {
            throw new FormatException(
                $"{messageName} payload must be exactly {ExchangePayloadSize} bytes.");
        }
    }

    private static void ValidateNonce(
        ReadOnlySpan<byte> nonce)
    {
        if (nonce.Length !=
            PairingCrypto.NonceSize)
        {
            throw new ArgumentException(
                $"Pairing nonce must be exactly {PairingCrypto.NonceSize} bytes.");
        }
    }

    private static void ValidatePublicKey(
        ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length !=
                PairingCrypto
                    .P256UncompressedPublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new ArgumentException(
                "Pairing public key must be a 65-byte uncompressed P-256 point.");
        }
    }

    private static void ValidateRole(
        PairingRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new FormatException(
                "Unknown pairing role.");
        }
    }

    private static void ValidateAbortReason(
        PairingAbortReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new FormatException(
                "Unknown pairing abort reason.");
        }
    }
}

public static class PairingFrameCodec
{
    public static byte[] EncodeOffer(
        PairingOfferPayload payload,
        ulong monotonicTimestampMicros = 0) =>
        Encode(
            MessageType.PairingOffer,
            PairingPayloadCodec
                .EncodeOffer(payload),
            monotonicTimestampMicros);

    public static PairingOfferPayload DecodeOffer(
        ReadOnlySpan<byte> frameBytes) =>
        PairingPayloadCodec.DecodeOffer(
            Decode(
                frameBytes,
                MessageType.PairingOffer));

    public static byte[] EncodeResponse(
        PairingResponsePayload payload,
        ulong monotonicTimestampMicros = 0) =>
        Encode(
            MessageType.PairingResponse,
            PairingPayloadCodec
                .EncodeResponse(payload),
            monotonicTimestampMicros);

    public static PairingResponsePayload DecodeResponse(
        ReadOnlySpan<byte> frameBytes) =>
        PairingPayloadCodec.DecodeResponse(
            Decode(
                frameBytes,
                MessageType.PairingResponse));

    public static byte[] EncodeConfirm(
        PairingConfirmPayload payload,
        ulong monotonicTimestampMicros = 0) =>
        Encode(
            MessageType.PairingConfirm,
            PairingPayloadCodec
                .EncodeConfirm(payload),
            monotonicTimestampMicros);

    public static PairingConfirmPayload DecodeConfirm(
        ReadOnlySpan<byte> frameBytes) =>
        PairingPayloadCodec.DecodeConfirm(
            Decode(
                frameBytes,
                MessageType.PairingConfirm));

    public static byte[] EncodeAbort(
        PairingAbortPayload payload,
        ulong monotonicTimestampMicros = 0) =>
        Encode(
            MessageType.PairingAbort,
            PairingPayloadCodec
                .EncodeAbort(payload),
            monotonicTimestampMicros);

    public static PairingAbortPayload DecodeAbort(
        ReadOnlySpan<byte> frameBytes) =>
        PairingPayloadCodec.DecodeAbort(
            Decode(
                frameBytes,
                MessageType.PairingAbort));

    private static byte[] Encode(
        MessageType messageType,
        byte[] payload,
        ulong monotonicTimestampMicros)
    {
        try
        {
            return ProtocolFrameCodec.Encode(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    messageType,
                    FrameFlags.None,
                    SessionId.Zero,
                    0,
                    monotonicTimestampMicros,
                    payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                payload);
        }
    }

    private static byte[] Decode(
        ReadOnlySpan<byte> frameBytes,
        MessageType expectedType)
    {
        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                frameBytes);

        if (frame.MessageType !=
                expectedType ||
            frame.Flags !=
                FrameFlags.None ||
            frame.SessionId !=
                SessionId.Zero ||
            frame.Sequence != 0)
        {
            throw new FormatException(
                $"Invalid {expectedType} pairing envelope.");
        }

        return frame.Payload;
    }
}
