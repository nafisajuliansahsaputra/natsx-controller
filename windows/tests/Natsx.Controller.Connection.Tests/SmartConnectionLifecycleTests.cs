using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class SmartConnectionLifecycleTests
{
    [Fact]
    public void LifecycleSnapshots_MapToManagerStatesBeforeActivation()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(
            timeProvider: clock);

        manager.Report(Snapshot(
            TransportRuntimeState.Connecting,
            TransportHealthGrade.Good,
            80));
        Assert.Equal(
            ConnectionManagerState.Connecting,
            manager.State);

        manager.Report(Snapshot(
            TransportRuntimeState.Authenticating,
            TransportHealthGrade.Good,
            80));
        Assert.Equal(
            ConnectionManagerState.Authenticating,
            manager.State);

        manager.Report(Snapshot(
            TransportRuntimeState.Stabilizing,
            TransportHealthGrade.Good,
            80));
        Assert.Equal(
            ConnectionManagerState.Stabilizing,
            manager.State);

        manager.Report(Snapshot(
            TransportRuntimeState.Ready,
            TransportHealthGrade.Good,
            90));
        Assert.Equal(
            ConnectionManagerState.Ready,
            manager.State);
    }

    [Fact]
    public void WarningActiveTransport_EntersSuspectWithoutPrematureFailover()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(
            timeProvider: clock);

        manager.Report(Snapshot(
            TransportRuntimeState.Ready,
            TransportHealthGrade.Good,
            90));

        HandoverProposal initial =
            Assert.IsType<HandoverProposal>(
                manager.Evaluate());

        manager.Commit(initial);

        manager.Report(Snapshot(
            TransportRuntimeState.Suspect,
            TransportHealthGrade.Warning,
            75));

        Assert.Null(manager.Evaluate());
        Assert.Equal(
            ConnectionManagerState.Suspect,
            manager.State);
        Assert.Equal(
            TransportKind.Wifi,
            manager.ActiveTransport);
    }

    [Fact]
    public void FailurePenalty_DecaysGraduallyAcrossWindow()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(
            timeProvider: clock);

        manager.Report(Snapshot(
            TransportRuntimeState.Ready,
            TransportHealthGrade.Good,
            100));

        int baseline =
            manager.GetEffectiveScore(
                TransportKind.Wifi);

        manager.RecordHardFailure(
            TransportKind.Wifi);

        int freshPenaltyScore =
            manager.GetEffectiveScore(
                TransportKind.Wifi);

        clock.Advance(TimeSpan.FromSeconds(15));

        int halfwayScore =
            manager.GetEffectiveScore(
                TransportKind.Wifi);

        clock.Advance(TimeSpan.FromSeconds(16));

        int expiredScore =
            manager.GetEffectiveScore(
                TransportKind.Wifi);

        Assert.True(freshPenaltyScore < baseline);
        Assert.True(halfwayScore > freshPenaltyScore);
        Assert.Equal(baseline, expiredScore);
    }

    private static TransportHealthSnapshot Snapshot(
        TransportRuntimeState state,
        TransportHealthGrade grade,
        int score) =>
        new(
            TransportKind.Wifi,
            state,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.Zero,
            score,
            grade);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            _timestamp;

        public void Advance(TimeSpan delta)
        {
            _timestamp += delta.Ticks;
        }
    }
}
