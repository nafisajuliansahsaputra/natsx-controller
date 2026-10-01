using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public readonly record struct PairingHelloPayload(
    PeerId AndroidPeerId,
    TransportCapabilities Capabilities,
    byte[] Nonce,
    byte[] PublicKey);

public readonly record struct PairingResponsePayload(
    PeerId WindowsPeerId,
    TransportCapabilities Capabilities,
    byte[] Nonce,
    byte[] PublicKey,
    byte[] ResponseProof);

public readonly record struct PairingConfirmationPayload(
    PeerId PeerId,
    byte[] Proof);

public static class PairingPayloadCodec
{
    public const int HelloPayloadSize = 117;
    public const int ResponsePayloadSize = 149;
    public const int ConfirmationPayloadSize = 48;

    private const TransportCapabilities AllowedCapabilities =
        TransportCapabilities.Wifi |
        TransportCapabilities.Bluetooth |
        TransportCapabilities.UsbDirect;

    public static byte[] EncodeHello(PairingHelloPayload payload)
    {
        ValidateCapabilities(payload.Capabilities);
        ValidateNonce(payload.Nonce);
        ValidatePublicKey(payload.PublicKey);

        var output = new byte[HelloPayloadSize];
        payload.AndroidPeerId.WriteBytes(output.AsSpan(0, 16));
        output[16] = (byte)payload.Capabilities;
        payload.Nonce.CopyTo(output, 20);
        payload.PublicKey.CopyTo(output, 52);
        return output;
    }

    public static PairingHelloPayload DecodeHello(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != HelloPayloadSize)
        {
            throw new FormatException($"PAIRING_HELLO payload must be exactly {HelloPayloadSize} bytes.");
        }

        EnsureReservedZero(payload[17..20]);
        var capabilities = (TransportCapabilities)payload[16];
        ValidateCapabilities(capabilities);

        byte[] nonce = payload[20..52].ToArray();
        byte[] publicKey = payload[52..117].ToArray();
        ValidateNonce(nonce);
        ValidatePublicKey(publicKey);

        return new PairingHelloPayload(
            PeerId.FromBytes(payload[..16]),
            capabilities,
            nonce,
            publicKey);
    }

    public static byte[] EncodeResponse(PairingResponsePayload payload)
    {
        ValidateCapabilities(payload.Capabilities);
        ValidateNonce(payload.Nonce);
        ValidatePublicKey(payload.PublicKey);
        ValidateProof(payload.ResponseProof);

        var output = new byte[ResponsePayloadSize];
        payload.WindowsPeerId.WriteBytes(output.AsSpan(0, 16));
        output[16] = (byte)payload.Capabilities;
        payload.Nonce.CopyTo(output, 20);
        payload.PublicKey.CopyTo(output, 52);
        payload.ResponseProof.CopyTo(output, 117);
        return output;
    }

    public static PairingResponsePayload DecodeResponse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ResponsePayloadSize)
        {
            throw new FormatException($"PAIRING_RESPONSE payload must be exactly {ResponsePayloadSize} bytes.");
        }

        EnsureReservedZero(payload[17..20]);
        var capabilities = (TransportCapabilities)payload[16];
        ValidateCapabilities(capabilities);

        byte[] nonce = payload[20..52].ToArray();
        byte[] publicKey = payload[52..117].ToArray();
        byte[] proof = payload[117..149].ToArray();

        ValidateNonce(nonce);
        ValidatePublicKey(publicKey);
        ValidateProof(proof);

        return new PairingResponsePayload(
            PeerId.FromBytes(payload[..16]),
            capabilities,
            nonce,
            publicKey,
            proof);
    }

    public static byte[] EncodeConfirmation(PairingConfirmationPayload payload)
    {
        ValidateProof(payload.Proof);
        var output = new byte[ConfirmationPayloadSize];
        payload.PeerId.WriteBytes(output.AsSpan(0, 16));
        payload.Proof.CopyTo(output, 16);
        return output;
    }

    public static PairingConfirmationPayload DecodeConfirmation(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ConfirmationPayloadSize)
        {
            throw new FormatException($"Pairing confirmation payload must be exactly {ConfirmationPayloadSize} bytes.");
        }

        return new PairingConfirmationPayload(
            PeerId.FromBytes(payload[..16]),
            payload[16..48].ToArray());
    }

    private static void ValidateCapabilities(TransportCapabilities capabilities)
    {
        if ((capabilities & ~AllowedCapabilities) != 0)
        {
            throw new FormatException("Pairing payload contains unknown transport capability bits.");
        }
    }

    private static void ValidateNonce(ReadOnlySpan<byte> nonce)
    {
        if (nonce.Length != PairingCrypto.NonceSize)
        {
            throw new FormatException("Pairing nonce must be exactly 32 bytes.");
        }
    }

    private static void ValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != PairingCrypto.P256UncompressedPublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new FormatException("Pairing public key must be 65-byte uncompressed P-256.");
        }
    }

    private static void ValidateProof(ReadOnlySpan<byte> proof)
    {
        if (proof.Length != PairingCrypto.DerivedKeySize)
        {
            throw new FormatException("Pairing proof must be exactly 32 bytes.");
        }
    }

    private static void EnsureReservedZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value != 0)
            {
                throw new FormatException("Pairing reserved bytes must be zero.");
            }
        }
    }
}
