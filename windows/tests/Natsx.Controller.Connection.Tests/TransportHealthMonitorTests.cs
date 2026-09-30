using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class TransportHealthMonitorTests
{
    [Fact]
    public void FastNormalLongWindows_UseDifferentSampleHorizons()
    {
        var clock = new ManualTimeProvider();
        var monitor = new TransportHealthMonitor(
            timeProvider: clock);

        monitor.Report(Snapshot(95, 5));
        clock.Advance(TimeSpan.FromMilliseconds(400));
        monitor.Report(Snapshot(85, 10));
        clock.Advance(TimeSpan.FromMilliseconds(400));
        monitor.Report(Snapshot(65, 25));

        Assert.Equal(2, monitor.GetFast(TransportKind.Wifi).SampleCount);
        Assert.Equal(3, monitor.GetNormal(TransportKind.Wifi).SampleCount);
        Assert.Equal(3, monitor.GetLong(TransportKind.Wifi).SampleCount);

        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(0, monitor.GetNormal(TransportKind.Wifi).SampleCount);
        Assert.Equal(3, monitor.GetLong(TransportKind.Wifi).SampleCount);
    }

    [Fact]
    public void LongWindow_TrimsSamplesOlderThanTenSeconds()
    {
        var clock = new ManualTimeProvider();
        var monitor = new TransportHealthMonitor(
            timeProvider: clock);

        monitor.Report(Snapshot(90, 5));
        clock.Advance(TimeSpan.FromSeconds(11));
        monitor.Report(Snapshot(80, 8));

        TransportHealthWindowSummary summary =
            monitor.GetLong(TransportKind.Wifi);

        Assert.Equal(1, summary.SampleCount);
        Assert.Equal(80, summary.AverageScore);
    }

    [Fact]
    public void SilenceTracker_TransitionsAcrossPolicyThresholds()
    {
        var clock = new ManualTimeProvider();
        var tracker = new TransportSilenceTracker(clock);

        tracker.MarkFreshPacket(TransportKind.Wifi);
        Assert.Equal(
            TransportHealthGrade.Excellent,
            tracker.Classify(TransportKind.Wifi));

        clock.Advance(TimeSpan.FromMilliseconds(30));
        Assert.Equal(
            TransportHealthGrade.Warning,
            tracker.Classify(TransportKind.Wifi));

        clock.Advance(TimeSpan.FromMilliseconds(30));
        Assert.Equal(
            TransportHealthGrade.Degraded,
            tracker.Classify(TransportKind.Wifi));

        clock.Advance(TimeSpan.FromMilliseconds(30));
        Assert.Equal(
            TransportHealthGrade.Critical,
            tracker.Classify(TransportKind.Wifi));

        clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.Equal(
            TransportHealthGrade.Lost,
            tracker.Classify(TransportKind.Wifi));
    }

    private static TransportHealthSnapshot Snapshot(
        int score,
        double rttMs) =>
        new(
            TransportKind.Wifi,
            TransportRuntimeState.Active,
            TimeSpan.FromMilliseconds(rttMs),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.FromMilliseconds(5),
            score,
            score >= 80
                ? TransportHealthGrade.Good
                : TransportHealthGrade.Degraded);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan amount)
        {
            _timestamp += amount.Ticks;
        }
    }
}
