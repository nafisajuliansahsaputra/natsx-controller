using System.Net;
using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiTrustedControlProcessor : IDisposable
{
    public const int MaximumPendingSessions = 16;
    public static readonly TimeSpan PendingSessionLifetime =
        TimeSpan.FromSeconds(5);

    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly TimeProvider _timeProvider;
    private readonly TransportLifecycle? _lifecycle;
    private readonly TrustedSessionRegistry? _sessionRegistry;
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, PendingSession> _pending = new();

    private bool _disposed;

    public WifiTrustedControlProcessor(
        PeerId localPeerId,
        TransportCapabilities capabilities =
            TransportCapabilities.Wifi |
            TransportCapabilities.Bluetooth |
            TransportCapabilities.UsbDirect,
        TimeProvider? timeProvider = null,
        TransportLifecycle? lifecycle = null,
        TrustedSessionRegistry? sessionRegistry = null)
    {
        if (!capabilities.HasFlag(TransportCapabilities.Wifi))
        {
            throw new ArgumentException(
                "Wi-Fi control processor must advertise Wi-Fi capability.",
                nameof(capabilities));
        }

        _localPeerId = localPeerId;
        _capabilities = capabilities;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifecycle = lifecycle;
        _sessionRegistry = sessionRegistry;
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public byte[] HandleChallenge(
        ReadOnlySpan<byte> datagram,
        IPEndPoint remoteEndPoint,
        Func<PeerId, byte[]?> trustSecretResolver,
        ulong monotonicTimestampMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        ArgumentNullException.ThrowIfNull(trustSecretResolver);

        WifiAuthChallenge challenge =
            WifiAuthDatagramCodec.DecodeChallenge(datagram);

        if (challenge.Payload.TargetPeerId != _localPeerId)
        {
            throw new CryptographicException(
                "AUTH_CHALLENGE target does not match this receiver.");
        }

        byte[]? trustSecret =
            trustSecretResolver(challenge.Payload.ChallengerPeerId);

        if (trustSecret is null)
        {
            throw new CryptographicException(
                "AUTH_CHALLENGE peer is not trusted.");
        }

        if (trustSecret.Length != TrustedReconnectCrypto.TrustKeySize)
        {
            CryptographicOperations.ZeroMemory(trustSecret);
            throw new CryptographicException(
                "Stored trust secret has an invalid length.");
        }

        try
        {
            var responder =
                new WifiTrustedHandshakeResponder(_localPeerId);

            using WifiTrustedHandshakeResponse response =
                responder.HandleChallenge(
                    datagram,
                    trustSecret,
                    monotonicTimestampMicros);

            var session = new WifiTrustedSession(
                response.Session.SessionId,
                CopySessionKey(response.Session));

            try
            {
                AddPending(
                    challenge.SessionId,
                    challenge.Payload.ChallengerPeerId,
                    remoteEndPoint,
                    session);

                MarkAuthenticating();

                return response.ResponseDatagram.ToArray();
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(trustSecret);
        }
    }

    public WifiTrustedControlCompletion HandleSessionReady(
        ReadOnlySpan<byte> datagram,
        IPEndPoint remoteEndPoint,
        ulong monotonicTimestampMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        SessionId sessionId = ReadSessionId(datagram);
        PendingSession pending = TakePending(sessionId, remoteEndPoint);

        try
        {
            SessionReadyPayload remote =
                WifiControlDatagramCodec.DecodeSessionReady(
                    datagram,
                    pending.Session);

            if (remote.Role != PeerRole.AndroidController ||
                remote.PeerId != pending.RemotePeerId ||
                !remote.Capabilities.HasFlag(TransportCapabilities.Wifi))
            {
                throw new CryptographicException(
                    "SESSION_READY identity, role, or capabilities do not match the trusted Wi-Fi peer.");
            }

            var local = new SessionReadyPayload(
                PeerRole.WindowsReceiver,
                _capabilities,
                _localPeerId);

            byte[] response =
                WifiControlDatagramCodec.EncodeSessionReady(
                    pending.Session,
                    local,
                    monotonicTimestampMicros);

            RegisterSession(
                pending.RemotePeerId,
                pending.Session);

            MarkStabilizing();

            return new WifiTrustedControlCompletion(
                pending.RemotePeerId,
                response,
                pending.Session);
        }
        catch
        {
            pending.Session.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            foreach (PendingSession pending in _pending.Values)
            {
                pending.Session.Dispose();
            }

            _pending.Clear();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void AddPending(
        SessionId sessionId,
        PeerId remotePeerId,
        IPEndPoint remoteEndPoint,
        WifiTrustedSession session)
    {
        lock (_gate)
        {
            PruneExpired();

            if (_pending.Remove(sessionId, out PendingSession? existing))
            {
                existing.Session.Dispose();
            }

            if (_pending.Count >= MaximumPendingSessions)
            {
                PendingSession oldest =
                    _pending.Values.MinBy(static value => value.CreatedTimestamp)!;

                _pending.Remove(oldest.Session.SessionId);
                oldest.Session.Dispose();
            }

            _pending.Add(
                sessionId,
                new PendingSession(
                    remotePeerId,
                    new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port),
                    session,
                    _timeProvider.GetTimestamp()));
        }
    }

    private PendingSession TakePending(
        SessionId sessionId,
        IPEndPoint remoteEndPoint)
    {
        lock (_gate)
        {
            PruneExpired();

            if (!_pending.Remove(sessionId, out PendingSession? pending))
            {
                throw new CryptographicException(
                    "SESSION_READY does not match a pending trusted handshake.");
            }

            if (!pending.RemoteEndPoint.Equals(remoteEndPoint))
            {
                pending.Session.Dispose();
                throw new CryptographicException(
                    "SESSION_READY endpoint does not match AUTH_CHALLENGE endpoint.");
            }

            return pending;
        }
    }

    private void PruneExpired()
    {
        long now = _timeProvider.GetTimestamp();

        foreach (SessionId sessionId in _pending
            .Where(pair =>
                _timeProvider.GetElapsedTime(
                    pair.Value.CreatedTimestamp,
                    now) > PendingSessionLifetime)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            PendingSession pending = _pending[sessionId];
            _pending.Remove(sessionId);
            pending.Session.Dispose();
        }
    }

    private void RegisterSession(
        PeerId remotePeerId,
        WifiTrustedSession session)
    {
        if (_sessionRegistry is null)
        {
            return;
        }

        byte[] key =
            CopySessionKey(session);

        try
        {
            _sessionRegistry.Replace(
                remotePeerId,
                session.SessionId,
                key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                key);
        }
    }

    private void MarkAuthenticating()
    {
        if (_lifecycle is null)
        {
            return;
        }

        if (_lifecycle.State is
            TransportRuntimeState.Unavailable or
            TransportRuntimeState.Available or
            TransportRuntimeState.Connecting or
            TransportRuntimeState.Authenticating or
            TransportRuntimeState.Failed)
        {
            _lifecycle.SetState(
                TransportRuntimeState.Authenticating);
        }
    }

    private void MarkStabilizing()
    {
        if (_lifecycle is null)
        {
            return;
        }

        if (_lifecycle.State is
            TransportRuntimeState.Unavailable or
            TransportRuntimeState.Available or
            TransportRuntimeState.Connecting or
            TransportRuntimeState.Authenticating or
            TransportRuntimeState.Stabilizing or
            TransportRuntimeState.Failed)
        {
            _lifecycle.SetState(
                TransportRuntimeState.Stabilizing);
        }
    }

    private static SessionId ReadSessionId(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < ProtocolConstants.HeaderSize)
        {
            throw new FormatException(
                "SESSION_READY datagram is shorter than the protocol header.");
        }

        return SessionId.FromBytes(datagram.Slice(12, SessionId.Size));
    }

    private static byte[] CopySessionKey(WifiTrustedSession session)
    {
        // Same assembly as WifiTrustedSession; take a defensive copy so the
        // temporary handshake response may be disposed independently.
        return session.SessionKey.ToArray();
    }

    private sealed record PendingSession(
        PeerId RemotePeerId,
        IPEndPoint RemoteEndPoint,
        WifiTrustedSession Session,
        long CreatedTimestamp);
}

public sealed record WifiTrustedControlCompletion(
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
