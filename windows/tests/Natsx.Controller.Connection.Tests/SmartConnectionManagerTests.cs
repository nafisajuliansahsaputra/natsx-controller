using Natsx.Controller.Core;
using Natsx.Controller.Connection;

namespace Natsx.Controller.Connection.Tests;

public sealed class SmartConnectionManagerTests
{
    [Fact]
    public void InitialSelection_PrefersHealthyWifiOverBluetooth()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.Report(Snapshot(TransportKind.Wifi, 90));
        manager.Report(Snapshot(TransportKind.Bluetooth, 95));

        HandoverProposal proposal = Assert.IsType<HandoverProposal>(manager.Evaluate());

        Assert.Equal(TransportKind.Wifi, proposal.To);
        Assert.Equal(HandoverReason.InitialSelection, proposal.Reason);

        manager.Commit(proposal);
        Assert.Equal(TransportKind.Wifi, manager.ActiveTransport);
    }

    [Fact]
    public void Usb_TakesOverOnlyAfterStabilization()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.Report(Snapshot(TransportKind.Wifi, 90));
        var initial = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(initial);

        manager.Report(Snapshot(TransportKind.Usb, 95));

        Assert.Null(manager.Evaluate());

        clock.Advance(TimeSpan.FromMilliseconds(499));
        manager.Report(Snapshot(TransportKind.Usb, 95));
        Assert.Null(manager.Evaluate());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        manager.Report(Snapshot(TransportKind.Usb, 95));

        HandoverProposal proposal = Assert.IsType<HandoverProposal>(manager.Evaluate());
        Assert.Equal(TransportKind.Usb, proposal.To);
        Assert.Equal(HandoverReason.PreferredUsbReady, proposal.Reason);
    }

    [Fact]
    public void DegradedWifi_FailsOverToHealthyBluetoothAfterSustainedWindow()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.Report(Snapshot(TransportKind.Wifi, 95));
        var initial = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(initial);

        manager.Report(Snapshot(TransportKind.Bluetooth, 90));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            55,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        Assert.Null(manager.Evaluate());

        clock.Advance(TimeSpan.FromMilliseconds(1499));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            55,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));
        manager.Report(Snapshot(TransportKind.Bluetooth, 90));
        Assert.Null(manager.Evaluate());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            55,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));
        manager.Report(Snapshot(TransportKind.Bluetooth, 90));

        HandoverProposal proposal = Assert.IsType<HandoverProposal>(manager.Evaluate());

        Assert.Equal(TransportKind.Bluetooth, proposal.To);
        Assert.Equal(HandoverReason.ActiveDegraded, proposal.Reason);
        Assert.True(proposal.FailureInduced);
    }

    [Fact]
    public void Cooldown_PreventsImmediateWifiHandback()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.Report(Snapshot(TransportKind.Wifi, 95));
        var initial = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(initial);

        manager.Report(Snapshot(TransportKind.Bluetooth, 95));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            50,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        manager.Report(Snapshot(TransportKind.Bluetooth, 95));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            50,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        var failover = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(failover);

        manager.Report(Snapshot(TransportKind.Wifi, 95));

        clock.Advance(TimeSpan.FromSeconds(3));
        manager.Report(Snapshot(TransportKind.Wifi, 95));
        manager.Report(Snapshot(TransportKind.Bluetooth, 90));

        Assert.Null(manager.Evaluate());

        clock.Advance(TimeSpan.FromSeconds(2));
        manager.Report(Snapshot(TransportKind.Wifi, 95));
        manager.Report(Snapshot(TransportKind.Bluetooth, 90));

        HandoverProposal handback = Assert.IsType<HandoverProposal>(manager.Evaluate());
        Assert.Equal(TransportKind.Wifi, handback.To);
        Assert.Equal(HandoverReason.PreferredWifiRecovered, handback.Reason);
    }

    [Fact]
    public void CriticalActiveTransport_OverridesCooldown()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.Report(Snapshot(TransportKind.Wifi, 95));
        var initial = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(initial);

        manager.Report(Snapshot(TransportKind.Bluetooth, 95));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            50,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        manager.Report(Snapshot(TransportKind.Bluetooth, 95));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            50,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        var toBluetooth = Assert.IsType<HandoverProposal>(manager.Evaluate());
        manager.Commit(toBluetooth);

        manager.Report(Snapshot(TransportKind.Wifi, 90));
        manager.Report(Snapshot(
            TransportKind.Bluetooth,
            30,
            TransportHealthGrade.Critical,
            TransportRuntimeState.Degraded));

        HandoverProposal emergency = Assert.IsType<HandoverProposal>(manager.Evaluate());

        Assert.Equal(TransportKind.Wifi, emergency.To);
        Assert.Equal(HandoverReason.ActiveCritical, emergency.Reason);
        Assert.True(emergency.FailureInduced);
    }

    [Fact]
    public void CircuitBreaker_BlocksRepeatedlyFailingCandidate()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);

        Assert.True(manager.IsCircuitOpen(TransportKind.Wifi));

        manager.Report(Snapshot(TransportKind.Wifi, 100));
        manager.Report(Snapshot(TransportKind.Bluetooth, 80));

        HandoverProposal proposal = Assert.IsType<HandoverProposal>(manager.Evaluate());

        Assert.Equal(TransportKind.Bluetooth, proposal.To);
    }

    [Fact]
    public void CircuitBreaker_PreservesFailurePenaltyWhileOpen()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);

        manager.Report(Snapshot(TransportKind.Wifi, 100));

        Assert.True(manager.IsCircuitOpen(TransportKind.Wifi));
        Assert.True(manager.GetEffectiveScore(TransportKind.Wifi) <= 65);
    }

    [Fact]
    public void CircuitBreaker_DoesNotExtendOnFailuresWhileAlreadyOpen()
    {
        var clock = new ManualTimeProvider();
        var manager = new SmartConnectionManager(timeProvider: clock);

        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);
        clock.Advance(TimeSpan.FromSeconds(1));
        manager.RecordHardFailure(TransportKind.Wifi);

        Assert.True(manager.IsCircuitOpen(TransportKind.Wifi));

        clock.Advance(TimeSpan.FromSeconds(10));
        manager.RecordHardFailure(TransportKind.Wifi);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(manager.IsCircuitOpen(TransportKind.Wifi));
    }

    private static TransportHealthSnapshot Snapshot(
        TransportKind transport,
        int score,
        TransportHealthGrade grade = TransportHealthGrade.Good,
        TransportRuntimeState state = TransportRuntimeState.Ready)
    {
        return new TransportHealthSnapshot(
            transport,
            state,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.Zero,
            score,
            grade);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            _timestamp += delta.Ticks;
        }
    }
}
