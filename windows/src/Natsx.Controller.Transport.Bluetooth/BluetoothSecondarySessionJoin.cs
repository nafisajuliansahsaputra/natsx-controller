using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth;

/// <summary>
/// Authenticates Bluetooth as a secondary transport for an already-active
/// logical controller session.
///
/// The incoming SessionId is used only as a registry lookup hint. The frame is
/// not trusted until its HMAC verifies with the registry key and its PeerId
/// matches the peer associated with that registry entry.
/// </summary>
public sealed class BluetoothSecondarySessionJoinServer
{
    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly TrustedSessionRegistry _sessionRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly TransportLifecycle? _lifecycle;

    public BluetoothSecondarySessionJoinServer(
        PeerId localPeerId,
        TrustedSessionRegistry sessionRegistry,
        TransportCapabilities capabilities =
            TransportCapabilities.Wifi |
            TransportCapabilities.Bluetooth |
            TransportCapabilities.UsbDirect,
        TimeProvider? timeProvider = null,
        TransportLifecycle? lifecycle = null)
    {
        ArgumentNullException.ThrowIfNull(
            sessionRegistry);

        if (!capabilities.HasFlag(
                TransportCapabilities.Bluetooth))
        {
            throw new ArgumentException(
                "Bluetooth secondary join must advertise Bluetooth capability.",
                nameof(capabilities));
        }

        _localPeerId = localPeerId;
        _sessionRegistry = sessionRegistry;
        _capabilities = capabilities;
        _timeProvider =
            timeProvider ??
            TimeProvider.System;
        _lifecycle = lifecycle;
    }

    public async ValueTask<BluetoothSecondarySessionJoinCompletion>
        JoinAsync(
            Stream inputStream,
            Stream outputStream,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            inputStream);
        ArgumentNullException.ThrowIfNull(
            outputStream);

        if (!inputStream.CanRead)
        {
            throw new ArgumentException(
                "Bluetooth secondary-join input stream must be readable.",
                nameof(inputStream));
        }

        if (!outputStream.CanWrite)
        {
            throw new ArgumentException(
                "Bluetooth secondary-join output stream must be writable.",
                nameof(outputStream));
        }

        MarkState(
            TransportRuntimeState.Authenticating);

        byte[] remoteReadyFrame =
            await BluetoothStreamFrameCodec
                .ReadFrameAsync(
                    inputStream,
                    cancellationToken)
                .ConfigureAwait(false);

        SessionId sessionId =
            ReadSessionIdHint(
                remoteReadyFrame);

        using TrustedSessionRegistration registration =
            _sessionRegistry.GetBySessionId(
                sessionId)
            ?? throw new CryptographicException(
                "Bluetooth secondary transport does not match an active trusted controller session.");

        byte[] sessionKey =
            registration.Material
                .CopySessionKey();

        try
        {
            using var trustedSession =
                new BluetoothTrustedSession(
                    sessionId,
                    sessionKey);

            SessionReadyPayload remoteReady =
                BluetoothControlFrameCodec
                    .DecodeSessionReady(
                        remoteReadyFrame,
                        trustedSession);

            ValidateRemoteReady(
                remoteReady,
                registration.PeerId);

            await WriteFramedAsync(
                outputStream,
                BluetoothControlFrameCodec
                    .EncodeSessionReady(
                        trustedSession,
                        new SessionReadyPayload(
                            PeerRole.WindowsReceiver,
                            _capabilities,
                            _localPeerId),
                        MonotonicMicros()),
                cancellationToken)
                .ConfigureAwait(false);

            byte[] remoteTransportReadyFrame =
                await BluetoothStreamFrameCodec
                    .ReadFrameAsync(
                        inputStream,
                        cancellationToken)
                    .ConfigureAwait(false);

            TransportReadyPayload remoteTransportReady =
                BluetoothControlFrameCodec
                    .DecodeTransportReady(
                        remoteTransportReadyFrame,
                        trustedSession);

            if (remoteTransportReady.Transport !=
                ProtocolTransport.Bluetooth)
            {
                throw new CryptographicException(
                    "Bluetooth secondary join received TRANSPORT_READY for a different transport.");
            }

            await WriteFramedAsync(
                outputStream,
                BluetoothControlFrameCodec
                    .EncodeTransportReady(
                        trustedSession,
                        ProtocolTransport.Bluetooth,
                        MonotonicMicros()),
                cancellationToken)
                .ConfigureAwait(false);

            MarkState(
                TransportRuntimeState.Stabilizing);

            return new BluetoothSecondarySessionJoinCompletion(
                registration.PeerId,
                new BluetoothTrustedSession(
                    sessionId,
                    sessionKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                sessionKey);
        }
    }

    private void ValidateRemoteReady(
        SessionReadyPayload remoteReady,
        PeerId expectedPeerId)
    {
        if (remoteReady.Role !=
                PeerRole.AndroidController ||
            remoteReady.PeerId !=
                expectedPeerId ||
            !remoteReady.Capabilities.HasFlag(
                TransportCapabilities.Bluetooth))
        {
            throw new CryptographicException(
                "Bluetooth secondary SESSION_READY identity, role, or capabilities do not match the active trusted peer.");
        }
    }

    private static SessionId ReadSessionIdHint(
        ReadOnlySpan<byte> frameBytes)
    {
        if (frameBytes.Length <
            ProtocolConstants.HeaderSize)
        {
            throw new FormatException(
                "Bluetooth secondary SESSION_READY is shorter than the protocol header.");
        }

        if (!frameBytes[..4]
            .SequenceEqual(
                ProtocolConstants.Magic))
        {
            throw new FormatException(
                "Bluetooth secondary SESSION_READY has invalid protocol magic.");
        }

        return SessionId.FromBytes(
            frameBytes.Slice(
                12,
                SessionId.Size));
    }

    private async ValueTask WriteFramedAsync(
        Stream outputStream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        byte[] packet =
            BluetoothStreamFrameCodec
                .Encode(frame);

        await outputStream.WriteAsync(
            packet,
            cancellationToken)
            .ConfigureAwait(false);

        await outputStream.FlushAsync(
            cancellationToken)
            .ConfigureAwait(false);
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
        _lifecycle?.SetState(state);
    }
}

public sealed record BluetoothSecondarySessionJoinCompletion(
    PeerId RemotePeerId,
    BluetoothTrustedSession Session) : IDisposable
{
    public void Dispose()
    {
        Session.Dispose();
    }
}
