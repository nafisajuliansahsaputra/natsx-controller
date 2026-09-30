using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothHealthTests
{
    private static readonly byte[] SessionKey =
        Enumerable.Range(0, 32)
            .Select(static value => (byte)value)
            .ToArray();

    [Fact]
    public void HeartbeatFrames_RoundTripAuthenticatedTimestamp()
    {
        using var session =
            new BluetoothTrustedSession(
                SessionId.CreateRandom(),
                SessionKey);

        const ulong probe = 123_456;

        byte[] heartbeat =
            BluetoothControlFrameCodec
                .EncodeHeartbeat(
                    session,
                    probe);

        Assert.Equal(
            probe,
            BluetoothControlFrameCodec
                .DecodeHeartbeat(
                    heartbeat,
                    session));

        byte[] ack =
            BluetoothControlFrameCodec
                .EncodeHeartbeatAck(
                    session,
                    responderTimestampMicros: 555_000,
                    echoedProbeTimestampMicros:
                        probe);

        Assert.Equal(
            probe,
            BluetoothControlFrameCodec
                .DecodeHeartbeatAck(
                    ack,
                    session));
    }

    [Fact]
    public void Tracker_RecordsBluetoothRttAndJitter()
    {
        var tracker =
            new BluetoothHealthTracker();

        tracker.RecordRoundTripTime(
            TimeSpan.FromMilliseconds(10));
        tracker.RecordRoundTripTime(
            TimeSpan.FromMilliseconds(22));

        BluetoothHealthSample sample =
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
