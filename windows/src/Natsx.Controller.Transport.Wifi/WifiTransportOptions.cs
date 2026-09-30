using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiTransportOptions
{
    public const int DefaultDiscoveryPort = 42160;
    public const int DefaultRealtimePort = 42161;

    public WifiTransportOptions(
        SessionId sessionId,
        ReadOnlySpan<byte> authenticationKey,
        ReadOnlySpan<byte> receiverId,
        string receiverName,
        int discoveryPort = DefaultDiscoveryPort,
        int realtimePort = DefaultRealtimePort)
    {
        if (authenticationKey.Length < 16)
        {
            throw new ArgumentException("Wi-Fi session authentication key must be at least 16 bytes.", nameof(authenticationKey));
        }

        if (receiverId.Length != 16)
        {
            throw new ArgumentException("Receiver ID must be exactly 16 bytes.", nameof(receiverId));
        }

        if (string.IsNullOrWhiteSpace(receiverName))
        {
            throw new ArgumentException("Receiver name is required.", nameof(receiverName));
        }

        if (discoveryPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort));
        }

        if (realtimePort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(realtimePort));
        }

        SessionId = sessionId;
        AuthenticationKey = authenticationKey.ToArray();
        ReceiverId = receiverId.ToArray();
        ReceiverName = receiverName.Trim();
        DiscoveryPort = discoveryPort;
        RealtimePort = realtimePort;
    }

    public SessionId SessionId { get; }

    public byte[] AuthenticationKey { get; }

    public byte[] ReceiverId { get; }

    public string ReceiverName { get; }

    public int DiscoveryPort { get; }

    public int RealtimePort { get; }
}
