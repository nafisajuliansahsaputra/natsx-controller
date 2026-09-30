using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiTrustedSession : IDisposable
{
    public const int SessionKeySize = 32;

    private readonly byte[] _sessionKey;
    private bool _disposed;

    public WifiTrustedSession(SessionId sessionId, ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != SessionKeySize)
        {
            throw new ArgumentException(
                $"Wi-Fi session key must be exactly {SessionKeySize} bytes.",
                nameof(sessionKey));
        }

        SessionId = sessionId;
        _sessionKey = sessionKey.ToArray();
    }

    public SessionId SessionId { get; }

    internal ReadOnlySpan<byte> SessionKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sessionKey;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_sessionKey);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
