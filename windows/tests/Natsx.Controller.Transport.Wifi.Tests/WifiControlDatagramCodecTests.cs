using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiControlDatagramCodecTests
{
    private static readonly byte[] Key =
        Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

    private static readonly SessionId Session =
        SessionId.FromBytes(Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF"));

    [Fact]
    public void HeartbeatRoundTrip_PreservesProbeTimestamp()
    {
        using var trusted = new WifiTrustedSession(Session, Key);

        byte[] heartbeat =
            WifiControlDatagramCodec.EncodeHeartbeat(
                trusted,
                monotonicTimestampMicros: 123_456);

        ulong probe =
            WifiControlDatagramCodec.DecodeHeartbeat(
                heartbeat,
                trusted);

        Assert.Equal(123_456ul, probe);

        byte[] ack =
            WifiControlDatagramCodec.EncodeHeartbeatAck(
                trusted,
                responderTimestampMicros: 777_000,
                echoedProbeTimestampMicros: probe);

        ulong echoed =
            WifiControlDatagramCodec.DecodeHeartbeatAck(
                ack,
                trusted);

        Assert.Equal(123_456ul, echoed);
    }
    [Fact]
    public void SessionReadyRoundTripPreservesTrustedIdentity()
    {
        byte[] key = Enumerable.Range(0, WifiTrustedSession.SessionKeySize)
            .Select(static value => (byte)value)
            .ToArray();

        using var trusted = new WifiTrustedSession(
            SessionId.FromBytes(Enumerable.Range(1, SessionId.Size)
                .Select(static value => (byte)value)
                .ToArray()),
            key);

        var payload = new SessionReadyPayload(
            PeerRole.AndroidController,
            TransportCapabilities.Wifi | TransportCapabilities.Bluetooth,
            PeerId.FromBytes(Enumerable.Range(20, PeerId.Size)
                .Select(static value => (byte)value)
                .ToArray()));

        byte[] encoded = WifiControlDatagramCodec.EncodeSessionReady(
            trusted,
            payload,
            123_456);

        SessionReadyPayload decoded =
            WifiControlDatagramCodec.DecodeSessionReady(encoded, trusted);

        Assert.Equal(payload, decoded);
    }

}
