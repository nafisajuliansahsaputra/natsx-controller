using System.Net;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed record WifiTransportOptions
{
    public int ListenPort { get; init; } = 37074;

    public SessionId SessionId { get; init; }

    public byte[] SessionKey { get; init; } = Array.Empty<byte>();

    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    public void Validate()
    {
        if (ListenPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ListenPort));

        if (SessionKey.Length != TrustedSessionCrypto.SessionKeySize)
            throw new ArgumentException("Wi-Fi transport requires a 32-byte session key.", nameof(SessionKey));
    }
}
