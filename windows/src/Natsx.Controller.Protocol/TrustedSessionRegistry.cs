using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

/// <summary>
/// Owns a defensive copy of one logical controller session's cryptographic
/// material. Callers receive copies so one transport cannot invalidate another
/// transport's session state by disposing its local adapter.
/// </summary>
public sealed class TrustedSessionMaterial : IDisposable
{
    private readonly byte[] _sessionKey;
    private bool _disposed;

    public TrustedSessionMaterial(
        SessionId sessionId,
        ReadOnlySpan<byte> sessionKey)
    {
        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "Trusted controller session requires a non-zero session ID.",
                nameof(sessionId));
        }

        if (sessionKey.Length !=
            TrustedReconnectCrypto.SessionKeySize)
        {
            throw new ArgumentException(
                $"Session key must be exactly {TrustedReconnectCrypto.SessionKeySize} bytes.",
                nameof(sessionKey));
        }

        SessionId = sessionId;
        _sessionKey = sessionKey.ToArray();
    }

    public SessionId SessionId { get; }

    public byte[] CopySessionKey()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        return _sessionKey.ToArray();
    }

    public TrustedSessionMaterial Clone()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        return new TrustedSessionMaterial(
            SessionId,
            _sessionKey);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(
            _sessionKey);

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Session-scoped cryptographic source of truth keyed by trusted peer identity.
///
/// Transport adapters keep their own short-lived session wrappers, but those
/// wrappers are created from copies of the same registry entry so Wi-Fi,
/// Bluetooth, and USB may share one SessionId/session key during handover.
/// </summary>
public sealed record TrustedSessionRegistration(
    PeerId PeerId,
    TrustedSessionMaterial Material) : IDisposable
{
    public void Dispose()
    {
        Material.Dispose();
    }
}

public sealed class TrustedSessionRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<PeerId, TrustedSessionMaterial>
        _sessions = new();

    private bool _disposed;

    public void Replace(
        PeerId peerId,
        SessionId sessionId,
        ReadOnlySpan<byte> sessionKey)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        var replacement =
            new TrustedSessionMaterial(
                sessionId,
                sessionKey);

        lock (_gate)
        {
            if (_sessions.Remove(
                    peerId,
                    out TrustedSessionMaterial? previous))
            {
                previous.Dispose();
            }

            _sessions.Add(
                peerId,
                replacement);
        }
    }

    public TrustedSessionMaterial? Get(
        PeerId peerId)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        lock (_gate)
        {
            return _sessions.TryGetValue(
                peerId,
                out TrustedSessionMaterial? material)
                ? material.Clone()
                : null;
        }
    }

    public TrustedSessionRegistration? GetBySessionId(
        SessionId sessionId)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (sessionId == SessionId.Zero)
        {
            return null;
        }

        lock (_gate)
        {
            foreach (
                KeyValuePair<PeerId, TrustedSessionMaterial> pair in
                _sessions)
            {
                if (pair.Value.SessionId == sessionId)
                {
                    return new TrustedSessionRegistration(
                        pair.Key,
                        pair.Value.Clone());
                }
            }

            return null;
        }
    }

    public void Remove(
        PeerId peerId)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        lock (_gate)
        {
            if (_sessions.Remove(
                    peerId,
                    out TrustedSessionMaterial? material))
            {
                material.Dispose();
            }
        }
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        lock (_gate)
        {
            ClearUnsafe();
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
            ClearUnsafe();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ClearUnsafe()
    {
        foreach (
            TrustedSessionMaterial material in
            _sessions.Values)
        {
            material.Dispose();
        }

        _sessions.Clear();
    }
}
