using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth;

public static class BluetoothAuthFrameCodec
{
    public static byte[] EncodeChallenge(
        SessionId sessionId,
        AuthChallengePayload payload,
        ulong monotonicTimestampMicros)
    {
        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Bluetooth trusted reconnect requires a non-zero session ID.",
                nameof(sessionId));
        }

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.AuthChallenge,
                FrameFlags.None,
                sessionId,
                0,
                monotonicTimestampMicros,
                AuthChallengePayloadCodec.Encode(payload)));
    }

    public static (SessionId SessionId, AuthChallengePayload Payload)
        DecodeChallenge(ReadOnlySpan<byte> frameBytes)
    {
        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);

        if (frame.MessageType != MessageType.AuthChallenge ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId == SessionId.Zero)
        {
            throw new FormatException(
                "Invalid Bluetooth AUTH_CHALLENGE envelope.");
        }

        return (
            frame.SessionId,
            AuthChallengePayloadCodec.Decode(frame.Payload));
    }

    public static byte[] EncodeResponse(
        SessionId sessionId,
        AuthResponsePayload payload,
        ulong monotonicTimestampMicros)
    {
        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Bluetooth trusted reconnect requires a non-zero session ID.",
                nameof(sessionId));
        }

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.AuthResponse,
                FrameFlags.None,
                sessionId,
                0,
                monotonicTimestampMicros,
                AuthResponsePayloadCodec.Encode(payload)));
    }

    public static (SessionId SessionId, AuthResponsePayload Payload)
        DecodeResponse(ReadOnlySpan<byte> frameBytes)
    {
        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);

        if (frame.MessageType != MessageType.AuthResponse ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId == SessionId.Zero)
        {
            throw new FormatException(
                "Invalid Bluetooth AUTH_RESPONSE envelope.");
        }

        return (
            frame.SessionId,
            AuthResponsePayloadCodec.Decode(frame.Payload));
    }
}

public static class BluetoothControlFrameCodec
{
    public const int HeartbeatAckPayloadSize = sizeof(ulong);

    public static byte[] EncodeSessionReady(
        BluetoothTrustedSession trustedSession,
        SessionReadyPayload payload,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.SessionReady,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                monotonicTimestampMicros,
                SessionReadyPayloadCodec.Encode(payload)),
            trustedSession.SessionKey);
    }

    public static SessionReadyPayload DecodeSessionReady(
        ReadOnlySpan<byte> frameBytes,
        BluetoothTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                frameBytes,
                trustedSession.SessionKey);

        if (frame.MessageType != MessageType.SessionReady ||
            !frame.Flags.HasFlag(FrameFlags.Authenticated) ||
            frame.SessionId != trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Invalid authenticated Bluetooth SESSION_READY frame.");
        }

        return SessionReadyPayloadCodec.Decode(frame.Payload);
    }

    public static byte[] EncodeHeartbeat(
        BluetoothTrustedSession trustedSession,
        ulong monotonicTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Heartbeat,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                monotonicTimestampMicros,
                Array.Empty<byte>()),
            trustedSession.SessionKey);
    }

    public static ulong DecodeHeartbeat(
        ReadOnlySpan<byte> frameBytes,
        BluetoothTrustedSession trustedSession)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                trustedSession);

        if (frame.MessageType != MessageType.Heartbeat)
        {
            throw new FormatException(
                $"Expected {MessageType.Heartbeat}, received {frame.MessageType}.");
        }

        if (frame.Payload.Length != 0)
        {
            throw new FormatException(
                "HEARTBEAT payload must be empty.");
        }

        return frame.MonotonicTimestampMicros;
    }

    public static byte[] EncodeHeartbeatAck(
        BluetoothTrustedSession trustedSession,
        ulong responderTimestampMicros,
        ulong echoedProbeTimestampMicros)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        var payload = new byte[HeartbeatAckPayloadSize];
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt64LittleEndian(
                payload,
                echoedProbeTimestampMicros);

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.HeartbeatAck,
                FrameFlags.Authenticated,
                trustedSession.SessionId,
                0,
                responderTimestampMicros,
                payload),
            trustedSession.SessionKey);
    }

    public static ulong DecodeHeartbeatAck(
        ReadOnlySpan<byte> frameBytes,
        BluetoothTrustedSession trustedSession)
    {
        ProtocolFrame frame =
            DecodeAuthenticatedFrame(
                frameBytes,
                trustedSession);

        if (frame.MessageType != MessageType.HeartbeatAck)
        {
            throw new FormatException(
                $"Expected {MessageType.HeartbeatAck}, received {frame.MessageType}.");
        }

        if (frame.Payload.Length != HeartbeatAckPayloadSize)
        {
            throw new FormatException(
                "HEARTBEAT_ACK payload must be exactly 8 bytes.");
        }

        return System.Buffers.Binary.BinaryPrimitives
            .ReadUInt64LittleEndian(frame.Payload);
    }

    internal static ProtocolFrame DecodeAuthenticatedFrame(
        ReadOnlySpan<byte> frameBytes,
        BluetoothTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                frameBytes,
                trustedSession.SessionKey);

        if (!frame.Flags.HasFlag(
                FrameFlags.Authenticated))
        {
            throw new CryptographicException(
                "Bluetooth session traffic must be authenticated.");
        }

        if (frame.SessionId !=
            trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Bluetooth frame belongs to a different controller session.");
        }

        return frame;
    }
}

public sealed class BluetoothTrustedHandshakeServer
{
    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly TimeProvider _timeProvider;
    private readonly TransportLifecycle? _lifecycle;
    private readonly TrustedSessionRegistry? _sessionRegistry;

    public BluetoothTrustedHandshakeServer(
        PeerId localPeerId,
        TransportCapabilities capabilities =
            TransportCapabilities.Wifi |
            TransportCapabilities.Bluetooth |
            TransportCapabilities.UsbDirect,
        TimeProvider? timeProvider = null,
        TransportLifecycle? lifecycle = null,
        TrustedSessionRegistry? sessionRegistry = null)
    {
        if (!capabilities.HasFlag(TransportCapabilities.Bluetooth))
        {
            throw new ArgumentException(
                "Bluetooth handshake must advertise Bluetooth capability.",
                nameof(capabilities));
        }

        _localPeerId = localPeerId;
        _capabilities = capabilities;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifecycle = lifecycle;
        _sessionRegistry = sessionRegistry;
    }

    public async ValueTask<BluetoothTrustedHandshakeCompletion>
        AuthenticateAsync(
            Stream inputStream,
            Stream outputStream,
            Func<PeerId, byte[]?> trustSecretResolver,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(trustSecretResolver);

        if (!inputStream.CanRead)
        {
            throw new ArgumentException(
                "Bluetooth handshake input stream must be readable.",
                nameof(inputStream));
        }

        if (!outputStream.CanWrite)
        {
            throw new ArgumentException(
                "Bluetooth handshake output stream must be writable.",
                nameof(outputStream));
        }

        byte[] challengeFrame =
            await BluetoothStreamFrameCodec.ReadFrameAsync(
                inputStream,
                cancellationToken).ConfigureAwait(false);

        var challenge =
            BluetoothAuthFrameCodec.DecodeChallenge(
                challengeFrame);

        if (challenge.Payload.TargetPeerId != _localPeerId)
        {
            throw new CryptographicException(
                "Bluetooth AUTH_CHALLENGE target does not match this receiver.");
        }

        byte[]? trustSecret =
            trustSecretResolver(
                challenge.Payload.ChallengerPeerId);

        if (trustSecret is null)
        {
            throw new CryptographicException(
                "Bluetooth AUTH_CHALLENGE peer is not trusted.");
        }

        if (trustSecret.Length != TrustedReconnectCrypto.TrustKeySize)
        {
            CryptographicOperations.ZeroMemory(trustSecret);
            throw new CryptographicException(
                "Stored Bluetooth trust secret has an invalid length.");
        }

        MarkState(TransportRuntimeState.Authenticating);

        try
        {
            byte[] proof =
                TrustedReconnectCrypto.CreateProof(
                    trustSecret,
                    challenge.SessionId,
                    challenge.Payload.ChallengeNonce,
                    challenge.Payload.ChallengerPeerId,
                    _localPeerId);

            byte[] sessionKey =
                TrustedReconnectCrypto.DeriveSessionKey(
                    trustSecret,
                    challenge.SessionId,
                    challenge.Payload.ChallengeNonce,
                    challenge.Payload.ChallengerPeerId,
                    _localPeerId);

            try
            {
                using var temporarySession =
                    new BluetoothTrustedSession(
                        challenge.SessionId,
                        sessionKey);

                byte[] responseFrame =
                    BluetoothAuthFrameCodec.EncodeResponse(
                        challenge.SessionId,
                        new AuthResponsePayload(
                            _localPeerId,
                            challenge.Payload.ChallengerPeerId,
                            proof),
                        MonotonicMicros());

                await WriteFramedAsync(
                    outputStream,
                    responseFrame,
                    cancellationToken).ConfigureAwait(false);

                byte[] remoteReadyFrame =
                    await BluetoothStreamFrameCodec.ReadFrameAsync(
                        inputStream,
                        cancellationToken).ConfigureAwait(false);

                SessionReadyPayload remoteReady =
                    BluetoothControlFrameCodec.DecodeSessionReady(
                        remoteReadyFrame,
                        temporarySession);

                if (remoteReady.Role != PeerRole.AndroidController ||
                    remoteReady.PeerId != challenge.Payload.ChallengerPeerId ||
                    !remoteReady.Capabilities.HasFlag(
                        TransportCapabilities.Bluetooth))
                {
                    throw new CryptographicException(
                        "Bluetooth SESSION_READY identity, role, or capabilities do not match the trusted peer.");
                }

                byte[] localReadyFrame =
                    BluetoothControlFrameCodec.EncodeSessionReady(
                        temporarySession,
                        new SessionReadyPayload(
                            PeerRole.WindowsReceiver,
                            _capabilities,
                            _localPeerId),
                        MonotonicMicros());

                await WriteFramedAsync(
                    outputStream,
                    localReadyFrame,
                    cancellationToken).ConfigureAwait(false);

                _sessionRegistry?.Replace(
                    challenge.Payload.ChallengerPeerId,
                    challenge.SessionId,
                    sessionKey);

                MarkState(TransportRuntimeState.Stabilizing);

                return new BluetoothTrustedHandshakeCompletion(
                    challenge.Payload.ChallengerPeerId,
                    new BluetoothTrustedSession(
                        challenge.SessionId,
                        sessionKey));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(proof);
                CryptographicOperations.ZeroMemory(sessionKey);
                CryptographicOperations.ZeroMemory(
                    challenge.Payload.ChallengeNonce);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(trustSecret);
        }
    }

    private async ValueTask WriteFramedAsync(
        Stream outputStream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        byte[] packet =
            BluetoothStreamFrameCodec.Encode(frame);

        await outputStream.WriteAsync(
            packet,
            cancellationToken).ConfigureAwait(false);

        await outputStream.FlushAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private ulong MonotonicMicros()
    {
        TimeSpan elapsed =
            _timeProvider.GetElapsedTime(
                0,
                _timeProvider.GetTimestamp());

        return checked(
            (ulong)(elapsed.Ticks / 10));
    }

    private void MarkState(
        TransportRuntimeState state)
    {
        if (_lifecycle is null)
        {
            return;
        }

        _lifecycle.SetState(state);
    }
}

public sealed record BluetoothTrustedHandshakeCompletion(
    PeerId RemotePeerId,
    BluetoothTrustedSession Session) : IDisposable
{
    public void Dispose()
    {
        Session.Dispose();
    }
}
