using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public readonly record struct WifiAuthChallenge(
    SessionId SessionId,
    AuthChallengePayload Payload);

public readonly record struct WifiAuthResponse(
    SessionId SessionId,
    AuthResponsePayload Payload);

public static class WifiAuthDatagramCodec
{
    public static byte[] EncodeChallenge(
        WifiAuthChallenge challenge,
        ulong monotonicTimestampMicros)
    {
        if (challenge.SessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Trusted reconnect requires a non-zero session ID.",
                nameof(challenge));
        }

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.AuthChallenge,
                FrameFlags.None,
                challenge.SessionId,
                0,
                monotonicTimestampMicros,
                AuthChallengePayloadCodec.Encode(challenge.Payload)));
    }

    public static WifiAuthChallenge DecodeChallenge(
        ReadOnlySpan<byte> datagram)
    {
        ProtocolFrame frame = ProtocolFrameCodec.Decode(datagram);

        if (frame.MessageType != MessageType.AuthChallenge ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId == SessionId.Zero)
        {
            throw new FormatException(
                "Invalid Wi-Fi AUTH_CHALLENGE envelope.");
        }

        return new WifiAuthChallenge(
            frame.SessionId,
            AuthChallengePayloadCodec.Decode(frame.Payload));
    }

    public static byte[] EncodeResponse(
        WifiAuthResponse response,
        ulong monotonicTimestampMicros)
    {
        if (response.SessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Trusted reconnect requires a non-zero session ID.",
                nameof(response));
        }

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.AuthResponse,
                FrameFlags.None,
                response.SessionId,
                0,
                monotonicTimestampMicros,
                AuthResponsePayloadCodec.Encode(response.Payload)));
    }

    public static WifiAuthResponse DecodeResponse(
        ReadOnlySpan<byte> datagram)
    {
        ProtocolFrame frame = ProtocolFrameCodec.Decode(datagram);

        if (frame.MessageType != MessageType.AuthResponse ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId == SessionId.Zero)
        {
            throw new FormatException(
                "Invalid Wi-Fi AUTH_RESPONSE envelope.");
        }

        return new WifiAuthResponse(
            frame.SessionId,
            AuthResponsePayloadCodec.Decode(frame.Payload));
    }
}

public sealed class WifiTrustedHandshakeChallenge : IDisposable
{
    private readonly byte[] _challengeNonce;
    private readonly byte[] _trustSecret;
    private bool _disposed;

    private WifiTrustedHandshakeChallenge(
        PeerId localPeerId,
        PeerId receiverPeerId,
        SessionId sessionId,
        byte[] challengeNonce,
        byte[] trustSecret)
    {
        LocalPeerId = localPeerId;
        ReceiverPeerId = receiverPeerId;
        SessionId = sessionId;
        _challengeNonce = challengeNonce;
        _trustSecret = trustSecret;
    }

    public PeerId LocalPeerId { get; }

    public PeerId ReceiverPeerId { get; }

    public SessionId SessionId { get; }

    public static WifiTrustedHandshakeChallenge Create(
        PeerId localPeerId,
        PeerId receiverPeerId,
        ReadOnlySpan<byte> trustSecret)
    {
        if (trustSecret.Length != TrustedReconnectCrypto.TrustKeySize)
        {
            throw new ArgumentException(
                $"Trust secret must be exactly {TrustedReconnectCrypto.TrustKeySize} bytes.",
                nameof(trustSecret));
        }

        return new WifiTrustedHandshakeChallenge(
            localPeerId,
            receiverPeerId,
            SessionId.CreateRandom(),
            AuthChallengePayloadCodec.CreateChallenge(),
            trustSecret.ToArray());
    }

    public byte[] EncodeChallenge(
        ulong monotonicTimestampMicros)
    {
        ThrowIfDisposed();

        return WifiAuthDatagramCodec.EncodeChallenge(
            new WifiAuthChallenge(
                SessionId,
                new AuthChallengePayload(
                    LocalPeerId,
                    ReceiverPeerId,
                    _challengeNonce.ToArray())),
            monotonicTimestampMicros);
    }

    public WifiTrustedSession AcceptResponse(
        ReadOnlySpan<byte> datagram)
    {
        ThrowIfDisposed();

        WifiAuthResponse response =
            WifiAuthDatagramCodec.DecodeResponse(datagram);

        if (response.SessionId != SessionId)
        {
            throw new CryptographicException(
                "AUTH_RESPONSE belongs to a different session.");
        }

        if (response.Payload.ResponderPeerId != ReceiverPeerId ||
            response.Payload.ChallengerPeerId != LocalPeerId)
        {
            throw new CryptographicException(
                "AUTH_RESPONSE peer identity mismatch.");
        }

        bool valid = TrustedReconnectCrypto.VerifyProof(
            _trustSecret,
            SessionId,
            _challengeNonce,
            LocalPeerId,
            ReceiverPeerId,
            response.Payload.Proof);

        if (!valid)
        {
            throw new CryptographicException(
                "AUTH_RESPONSE proof is invalid.");
        }

        byte[] sessionKey = TrustedReconnectCrypto.DeriveSessionKey(
            _trustSecret,
            SessionId,
            _challengeNonce,
            LocalPeerId,
            ReceiverPeerId);

        try
        {
            return new WifiTrustedSession(SessionId, sessionKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_challengeNonce);
        CryptographicOperations.ZeroMemory(_trustSecret);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class WifiTrustedHandshakeResponder
{
    private readonly PeerId _localPeerId;

    public WifiTrustedHandshakeResponder(PeerId localPeerId)
    {
        _localPeerId = localPeerId;
    }

    public WifiTrustedHandshakeResponse HandleChallenge(
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> trustSecret,
        ulong monotonicTimestampMicros)
    {
        WifiAuthChallenge challenge =
            WifiAuthDatagramCodec.DecodeChallenge(datagram);

        if (challenge.Payload.TargetPeerId != _localPeerId)
        {
            throw new CryptographicException(
                "AUTH_CHALLENGE target does not match this receiver.");
        }

        byte[] proof = TrustedReconnectCrypto.CreateProof(
            trustSecret,
            challenge.SessionId,
            challenge.Payload.ChallengeNonce,
            challenge.Payload.ChallengerPeerId,
            _localPeerId);

        byte[] sessionKey = TrustedReconnectCrypto.DeriveSessionKey(
            trustSecret,
            challenge.SessionId,
            challenge.Payload.ChallengeNonce,
            challenge.Payload.ChallengerPeerId,
            _localPeerId);

        try
        {
            byte[] response = WifiAuthDatagramCodec.EncodeResponse(
                new WifiAuthResponse(
                    challenge.SessionId,
                    new AuthResponsePayload(
                        _localPeerId,
                        challenge.Payload.ChallengerPeerId,
                        proof)),
                monotonicTimestampMicros);

            var session = new WifiTrustedSession(
                challenge.SessionId,
                sessionKey);

            return new WifiTrustedHandshakeResponse(
                challenge.Payload.ChallengerPeerId,
                response,
                session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(proof);
            CryptographicOperations.ZeroMemory(sessionKey);
            CryptographicOperations.ZeroMemory(
                challenge.Payload.ChallengeNonce);
        }
    }
}

public sealed record WifiTrustedHandshakeResponse(
    PeerId RemotePeerId,
    byte[] ResponseDatagram,
    WifiTrustedSession Session) : IDisposable
{
    public void Dispose()
    {
        Session.Dispose();
        CryptographicOperations.ZeroMemory(ResponseDatagram);
    }
}
