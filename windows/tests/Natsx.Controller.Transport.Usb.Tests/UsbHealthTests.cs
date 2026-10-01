using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbHealthTests
{
    private static readonly byte[] SessionKey =
        Enumerable.Range(0, 32)
            .Select(static value => (byte)value)
            .ToArray();

    [Fact]
    public void HeartbeatFrames_RoundTripAuthenticatedTimestamp()
    {
        using var session =
            new UsbTrustedSession(
                SessionId.CreateRandom(),
                SessionKey);

        const ulong probe = 123_456;

        byte[] heartbeat =
            UsbControlFrameCodec
                .EncodeHeartbeat(
                    session,
                    probe);

        Assert.Equal(
            probe,
            UsbControlFrameCodec
                .DecodeHeartbeat(
                    heartbeat,
                    session));

        byte[] ack =
            UsbControlFrameCodec
                .EncodeHeartbeatAck(
                    session,
                    responderTimestampMicros: 555_000,
                    echoedProbeTimestampMicros:
                        probe);

        Assert.Equal(
            probe,
            UsbControlFrameCodec
                .DecodeHeartbeatAck(
                    ack,
                    session));
    }

    [Fact]
    public void Tracker_RecordsUsbRttAndJitter()
    {
        var tracker =
            new UsbHealthTracker();

        tracker.RecordRoundTripTime(
            TimeSpan.FromMilliseconds(10));
        tracker.RecordRoundTripTime(
            TimeSpan.FromMilliseconds(22));

        UsbHealthSample sample =
            tracker.Snapshot();

        Assert.Equal(
            TimeSpan.FromMilliseconds(22),
            sample.RoundTripTime);

        Assert.True(
            sample.Jitter >
            TimeSpan.Zero);

        Assert.Equal(
            0,
            tracker.ToTransportMetrics(
                Natsx.Controller.Connection
                    .TransportRuntimeState.Ready,
                TimeSpan.Zero)
                .PacketLossPercent);
    }
}
