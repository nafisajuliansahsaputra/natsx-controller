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
}
