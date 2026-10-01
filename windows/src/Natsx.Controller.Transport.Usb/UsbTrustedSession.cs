using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public sealed class UsbTrustedSession : IDisposable
{
    public const int SessionKeySize = 32;

    private readonly byte[] _sessionKey;
    private bool _disposed;

    public UsbTrustedSession(
        SessionId sessionId,
        ReadOnlySpan<byte> sessionKey)
    {
        if (sessionId == SessionId.Zero)
        {
            throw new ArgumentException(
                "USB trusted session requires a non-zero session ID.",
                nameof(sessionId));
        }

        if (sessionKey.Length != SessionKeySize)
        {
            throw new ArgumentException(
                $"USB session key must be exactly {SessionKeySize} bytes.",
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
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            return _sessionKey;
        }
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
