using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Natsx.Controller.Protocol;

public static class PairingCrypto
{
    public const int SecretSize = 32;
    public const int NonceSize = 16;
    public const int ProofSize = 16;
    public const int DerivedKeySize = 32;

    private static readonly byte[] PairRequestLabel =
        Encoding.ASCII.GetBytes("NATSX-PAIR-REQUEST-V1");
    private static readonly byte[] TrustLabel =
        Encoding.ASCII.GetBytes("NATSX-TRUST-V1");
    private static readonly byte[] PairResponseLabel =
        Encoding.ASCII.GetBytes("NATSX-PAIR-RESPONSE-V1");
    private static readonly byte[] AuthChallengeLabel =
        Encoding.ASCII.GetBytes("NATSX-AUTH-CHALLENGE-V1");
    private static readonly byte[] SessionLabel =
        Encoding.ASCII.GetBytes("NATSX-SESSION-V1");
    private static readonly byte[] AuthResponseLabel =
        Encoding.ASCII.GetBytes("NATSX-AUTH-RESPONSE-V1");
    private static readonly byte[] SessionReadyLabel =
        Encoding.ASCII.GetBytes("NATSX-SESSION-READY-V1");

    public static byte[] CreatePairRequestProof(
        ReadOnlySpan<byte> pairingSecret,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        long expiresUnixSeconds)
    {
        ValidateSecret(pairingSecret, nameof(pairingSecret));
        ValidateNonce(clientNonce, nameof(clientNonce));

        Span<byte> receiver = stackalloc byte[PeerId.Size];
        Span<byte> controller = stackalloc byte[PeerId.Size];
        receiverPeerId.WriteBytes(receiver);
        controllerPeerId.WriteBytes(controller);

        byte[] transcript = new byte[
            PairRequestLabel.Length +
            PeerId.Size +
            PeerId.Size +
            NonceSize +
            sizeof(long)];

        int offset = 0;
        PairRequestLabel.CopyTo(transcript, offset);
        offset += PairRequestLabel.Length;
        receiver.CopyTo(transcript.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;
        controller.CopyTo(transcript.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;
        clientNonce.CopyTo(transcript.AsSpan(offset, NonceSize));
        offset += NonceSize;
        BinaryPrimitives.WriteInt64LittleEndian(
            transcript.AsSpan(offset, sizeof(long)),
            expiresUnixSeconds);

        return TruncatedHmac(pairingSecret, transcript);
    }

    public static byte[] DeriveTrustKey(
        ReadOnlySpan<byte> pairingSecret,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        ValidateSecret(pairingSecret, nameof(pairingSecret));
        ValidateNonce(clientNonce, nameof(clientNonce));
        ValidateNonce(serverNonce, nameof(serverNonce));

        byte[] salt = new byte[NonceSize * 2];
        clientNonce.CopyTo(salt.AsSpan(0, NonceSize));
        serverNonce.CopyTo(salt.AsSpan(NonceSize, NonceSize));

        byte[] info = PeerInfo(TrustLabel, receiverPeerId, controllerPeerId);
        return HkdfSha256(pairingSecret, salt, info);
    }

    public static byte[] CreatePairResponseProof(
        ReadOnlySpan<byte> trustKey,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        ValidateDerivedKey(trustKey, nameof(trustKey));
        ValidateNonce(clientNonce, nameof(clientNonce));
        ValidateNonce(serverNonce, nameof(serverNonce));

        byte[] transcript = PeerInfoWithNonces(
            PairResponseLabel,
            receiverPeerId,
            controllerPeerId,
            clientNonce,
            serverNonce);

        return TruncatedHmac(trustKey, transcript);
    }

    public static byte[] CreateAuthChallengeProof(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce)
    {
        ValidateDerivedKey(trustKey, nameof(trustKey));
        ValidateNonce(clientNonce, nameof(clientNonce));

        byte[] transcript = SessionPeerInfoWithNonces(
            AuthChallengeLabel,
            sessionId,
            receiverPeerId,
            controllerPeerId,
            clientNonce,
            ReadOnlySpan<byte>.Empty);

        return TruncatedHmac(trustKey, transcript);
    }

    public static byte[] CreateAuthResponseProof(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        ValidateDerivedKey(trustKey, nameof(trustKey));
        ValidateNonce(clientNonce, nameof(clientNonce));
        ValidateNonce(serverNonce, nameof(serverNonce));

        byte[] transcript = SessionPeerInfoWithNonces(
            AuthResponseLabel,
            sessionId,
            receiverPeerId,
            controllerPeerId,
            clientNonce,
            serverNonce);

        return TruncatedHmac(trustKey, transcript);
    }

    public static byte[] DeriveSessionKey(
        ReadOnlySpan<byte> trustKey,
        SessionId sessionId,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        ValidateDerivedKey(trustKey, nameof(trustKey));
        ValidateNonce(clientNonce, nameof(clientNonce));
        ValidateNonce(serverNonce, nameof(serverNonce));

        byte[] salt = new byte[NonceSize * 2];
        clientNonce.CopyTo(salt.AsSpan(0, NonceSize));
        serverNonce.CopyTo(salt.AsSpan(NonceSize, NonceSize));

        Span<byte> session = stackalloc byte[SessionId.Size];
        sessionId.WriteBytes(session);

        byte[] info = new byte[
            SessionLabel.Length +
            SessionId.Size +
            PeerId.Size +
            PeerId.Size];

        int offset = 0;
        SessionLabel.CopyTo(info, offset);
        offset += SessionLabel.Length;
        session.CopyTo(info.AsSpan(offset, SessionId.Size));
        offset += SessionId.Size;

        Span<byte> receiver = stackalloc byte[PeerId.Size];
        Span<byte> controller = stackalloc byte[PeerId.Size];
        receiverPeerId.WriteBytes(receiver);
        controllerPeerId.WriteBytes(controller);
        receiver.CopyTo(info.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;
        controller.CopyTo(info.AsSpan(offset, PeerId.Size));

        return HkdfSha256(trustKey, salt, info);
    }

    public static byte[] CreateSessionReadyProof(
        ReadOnlySpan<byte> sessionKey,
        SessionId sessionId)
    {
        ValidateDerivedKey(sessionKey, nameof(sessionKey));

        Span<byte> session = stackalloc byte[SessionId.Size];
        sessionId.WriteBytes(session);

        byte[] transcript = new byte[SessionReadyLabel.Length + SessionId.Size];
        SessionReadyLabel.CopyTo(transcript, 0);
        session.CopyTo(transcript.AsSpan(SessionReadyLabel.Length));

        return TruncatedHmac(sessionKey, transcript);
    }

    public static bool FixedTimeEquals(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual) =>
        expected.Length == actual.Length &&
        CryptographicOperations.FixedTimeEquals(expected, actual);

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> inputKey,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        byte[] prk = HMACSHA256.HashData(salt, inputKey);
        byte[] blockInput = new byte[info.Length + 1];
        info.CopyTo(blockInput);
        blockInput[^1] = 1;

        try
        {
            byte[] block = HMACSHA256.HashData(prk, blockInput);
            return block[..DerivedKeySize];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }

    private static byte[] TruncatedHmac(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> data)
    {
        byte[] full = HMACSHA256.HashData(key, data);
        try
        {
            return full[..ProofSize];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(full);
        }
    }

    private static byte[] PeerInfo(
        ReadOnlySpan<byte> label,
        PeerId receiverPeerId,
        PeerId controllerPeerId)
    {
        byte[] output = new byte[label.Length + PeerId.Size + PeerId.Size];
        label.CopyTo(output);

        Span<byte> receiver = output.AsSpan(label.Length, PeerId.Size);
        Span<byte> controller = output.AsSpan(label.Length + PeerId.Size, PeerId.Size);
        receiverPeerId.WriteBytes(receiver);
        controllerPeerId.WriteBytes(controller);
        return output;
    }

    private static byte[] PeerInfoWithNonces(
        ReadOnlySpan<byte> label,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        byte[] baseInfo = PeerInfo(label, receiverPeerId, controllerPeerId);
        byte[] output = new byte[baseInfo.Length + clientNonce.Length + serverNonce.Length];
        baseInfo.CopyTo(output, 0);
        clientNonce.CopyTo(output.AsSpan(baseInfo.Length));
        serverNonce.CopyTo(output.AsSpan(baseInfo.Length + clientNonce.Length));
        return output;
    }

    private static byte[] SessionPeerInfoWithNonces(
        ReadOnlySpan<byte> label,
        SessionId sessionId,
        PeerId receiverPeerId,
        PeerId controllerPeerId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        Span<byte> session = stackalloc byte[SessionId.Size];
        sessionId.WriteBytes(session);

        byte[] output = new byte[
            label.Length +
            SessionId.Size +
            PeerId.Size +
            PeerId.Size +
            clientNonce.Length +
            serverNonce.Length];

        int offset = 0;
        label.CopyTo(output);
        offset += label.Length;
        session.CopyTo(output.AsSpan(offset, SessionId.Size));
        offset += SessionId.Size;

        Span<byte> receiver = stackalloc byte[PeerId.Size];
        Span<byte> controller = stackalloc byte[PeerId.Size];
        receiverPeerId.WriteBytes(receiver);
        controllerPeerId.WriteBytes(controller);
        receiver.CopyTo(output.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;
        controller.CopyTo(output.AsSpan(offset, PeerId.Size));
        offset += PeerId.Size;

        clientNonce.CopyTo(output.AsSpan(offset));
        offset += clientNonce.Length;
        serverNonce.CopyTo(output.AsSpan(offset));
        return output;
    }

    private static void ValidateSecret(ReadOnlySpan<byte> value, string paramName)
    {
        if (value.Length != SecretSize)
        {
            throw new ArgumentException(
                $"Pairing secret must be exactly {SecretSize} bytes.",
                paramName);
        }
    }

    private static void ValidateNonce(ReadOnlySpan<byte> value, string paramName)
    {
        if (value.Length != NonceSize)
        {
            throw new ArgumentException(
                $"Nonce must be exactly {NonceSize} bytes.",
                paramName);
        }
    }

    private static void ValidateDerivedKey(ReadOnlySpan<byte> value, string paramName)
    {
        if (value.Length != DerivedKeySize)
        {
            throw new ArgumentException(
                $"Key must be exactly {DerivedKeySize} bytes.",
                paramName);
        }
    }
}
