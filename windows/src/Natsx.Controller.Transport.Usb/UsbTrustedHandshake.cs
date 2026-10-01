using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public static class UsbAuthFrameCodec
{
    public static byte[] EncodeChallenge(
        SessionId sessionId,
        AuthChallengePayload payload,
        ulong monotonicTimestampMicros)
    {
        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Usb trusted reconnect requires a non-zero session ID.",
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
                "Invalid Usb AUTH_CHALLENGE envelope.");
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
                "Usb trusted reconnect requires a non-zero session ID.",
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
                "Invalid Usb AUTH_RESPONSE envelope.");
        }

        return (
            frame.SessionId,
            AuthResponsePayloadCodec.Decode(frame.Payload));
    }
}

public sealed class UsbTrustedHandshakeServer
{
    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly TimeProvider _timeProvider;
    private readonly TransportLifecycle? _lifecycle;
    private readonly TrustedSessionRegistry? _sessionRegistry;

    public UsbTrustedHandshakeServer(
        PeerId localPeerId,
        TransportCapabilities capabilities =
            TransportCapabilities.Wifi |
            TransportCapabilities.UsbDirect |
            TransportCapabilities.UsbDirect,
        TimeProvider? timeProvider = null,
        TransportLifecycle? lifecycle = null,
        TrustedSessionRegistry? sessionRegistry = null)
    {
        if (!capabilities.HasFlag(TransportCapabilities.UsbDirect))
        {
            throw new ArgumentException(
                "Usb handshake must advertise Usb capability.",
                nameof(capabilities));
        }

        _localPeerId = localPeerId;
        _capabilities = capabilities;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifecycle = lifecycle;
        _sessionRegistry = sessionRegistry;
    }

    public async ValueTask<UsbTrustedHandshakeCompletion>
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
                "Usb handshake input stream must be readable.",
                nameof(inputStream));
        }

        if (!outputStream.CanWrite)
        {
            throw new ArgumentException(
                "Usb handshake output stream must be writable.",
                nameof(outputStream));
        }

        byte[] challengeFrame =
            await UsbStreamFrameCodec.ReadFrameAsync(
                inputStream,
                cancellationToken).ConfigureAwait(false);

        var challenge =
            UsbAuthFrameCodec.DecodeChallenge(
                challengeFrame);

        if (challenge.Payload.TargetPeerId != _localPeerId)
        {
            throw new CryptographicException(
                "Usb AUTH_CHALLENGE target does not match this receiver.");
        }

        byte[]? trustSecret =
            trustSecretResolver(
                challenge.Payload.ChallengerPeerId);

        if (trustSecret is null)
        {
            throw new CryptographicException(
                "Usb AUTH_CHALLENGE peer is not trusted.");
        }

        if (trustSecret.Length != TrustedReconnectCrypto.TrustKeySize)
        {
            CryptographicOperations.ZeroMemory(trustSecret);
            throw new CryptographicException(
                "Stored Usb trust secret has an invalid length.");
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
                    new UsbTrustedSession(
                        challenge.SessionId,
                        sessionKey);

                byte[] responseFrame =
                    UsbAuthFrameCodec.EncodeResponse(
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
                    await UsbStreamFrameCodec.ReadFrameAsync(
                        inputStream,
                        cancellationToken).ConfigureAwait(false);

                SessionReadyPayload remoteReady =
                    UsbControlFrameCodec.DecodeSessionReady(
                        remoteReadyFrame,
                        temporarySession);

                if (remoteReady.Role != PeerRole.AndroidController ||
                    remoteReady.PeerId != challenge.Payload.ChallengerPeerId ||
                    !remoteReady.Capabilities.HasFlag(
                        TransportCapabilities.UsbDirect))
                {
                    throw new CryptographicException(
                        "Usb SESSION_READY identity, role, or capabilities do not match the trusted peer.");
                }

                byte[] localReadyFrame =
                    UsbControlFrameCodec.EncodeSessionReady(
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

                return new UsbTrustedHandshakeCompletion(
                    challenge.Payload.ChallengerPeerId,
                    new UsbTrustedSession(
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
            UsbStreamFrameCodec.Encode(frame);

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

public sealed record UsbTrustedHandshakeCompletion(
    PeerId RemotePeerId,
    UsbTrustedSession Session) : IDisposable
{
    public void Dispose()
    {
        Session.Dispose();
    }
}
