using System.Net;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed record WifiTransportOptions
{
    public int ListenPort { get; init; } = 37074;

    public SessionId SessionId { get; init; }

    public byte[] SessionKey { get; init; } = Array.Empty<byte>();

    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    public bool HasPreAuthenticatedSession =>
        SessionId != Natsx.Controller.Protocol.SessionId.Zero &&
        SessionKey.Length == TrustedSessionCrypto.SessionKeySize;

    public void Validate()
    {
        if (ListenPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ListenPort));

        bool hasSessionId = SessionId != Natsx.Controller.Protocol.SessionId.Zero;
        bool hasSessionKey = SessionKey.Length != 0;

        if (hasSessionId != hasSessionKey)
        {
            throw new ArgumentException(
                "Session ID and session key must either both be supplied or both be omitted.");
        }

        if (hasSessionKey &&
            SessionKey.Length != TrustedSessionCrypto.SessionKeySize)
        {
            throw new ArgumentException(
                "Wi-Fi session key must be exactly 32 bytes.",
                nameof(SessionKey));
        }
    }
}
