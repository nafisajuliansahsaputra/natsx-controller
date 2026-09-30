namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiHealthTrackerTests
{
    [Fact]
    public void SequentialPackets_ReportZeroLoss()
    {
        var clock = new ManualTimeProvider();
        var tracker = new WifiHealthTracker(clock);

        tracker.RecordState(10);
        clock.Advance(TimeSpan.FromMilliseconds(8));
        tracker.RecordState(11);
        clock.Advance(TimeSpan.FromMilliseconds(8));
        tracker.RecordState(12);

        WifiHealthSample sample = tracker.Snapshot();

        Assert.Equal(0, sample.PacketLossPercent);
    }

    [Fact]
    public void SequenceGap_ReportsMissingPackets()
    {
        var clock = new ManualTimeProvider();
        var tracker = new WifiHealthTracker(clock);

        tracker.RecordState(10);
        clock.Advance(TimeSpan.FromMilliseconds(8));
        tracker.RecordState(13);

        WifiHealthSample sample = tracker.Snapshot();

        // Expected packets: first sample + sequence delta 3 = 4.
        // Received packets: first sample + current sample = 2.
        Assert.Equal(50, sample.PacketLossPercent, precision: 3);
    }

    [Fact]
    public void LossWindow_ExpiresOldGap()
    {
        var clock = new ManualTimeProvider();
        var tracker = new WifiHealthTracker(
            clock,
            lossWindow: TimeSpan.FromSeconds(2));

        tracker.RecordState(1);
        tracker.RecordState(4);

        Assert.True(tracker.Snapshot().PacketLossPercent > 0);

        clock.Advance(TimeSpan.FromSeconds(2.1));
        tracker.RecordState(5);

        Assert.Equal(0, tracker.Snapshot().PacketLossPercent);
    }

    [Fact]
    public void RttVariation_ProducesJitter()
    {
        var tracker = new WifiHealthTracker();

        tracker.RecordRoundTripTime(TimeSpan.FromMilliseconds(4));
        tracker.RecordRoundTripTime(TimeSpan.FromMilliseconds(10));

        WifiHealthSample sample = tracker.Snapshot();

        Assert.Equal(TimeSpan.FromMilliseconds(10), sample.RoundTripTime);
        Assert.True(sample.Jitter > TimeSpan.Zero);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }
    }
}
