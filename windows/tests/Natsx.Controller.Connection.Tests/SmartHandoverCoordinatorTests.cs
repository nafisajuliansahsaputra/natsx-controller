using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class SmartHandoverCoordinatorTests
{
    [Fact]
    public void HealthyHandover_SynchronizesThenAtomicallyMovesAuthority()
    {
        var clock = new ManualTimeProvider();
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        var manager = new SmartConnectionManager(
            timeProvider: clock);
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive,
            clock);
        var coordinator =
            new SmartHandoverCoordinator(
                manager,
                session,
                safety);

        manager.Report(Snapshot(
            TransportKind.Wifi,
            90));

        HandoverProposal initial =
            Assert.IsType<HandoverProposal>(
                manager.Evaluate());

        coordinator.ActivateInitial(initial);

        Assert.True(safety.TryAccept(
            TransportKind.Wifi,
            100,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            }));

        // Make Bluetooth a clearly better candidate long enough to pass
        // normal switch hysteresis.
        manager.Report(Snapshot(
            TransportKind.Bluetooth,
            100));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            60,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        clock.Advance(
            TimeSpan.FromMilliseconds(1500));

        manager.Report(Snapshot(
            TransportKind.Bluetooth,
            100));
        manager.Report(Snapshot(
            TransportKind.Wifi,
            60,
            TransportHealthGrade.Degraded,
            TransportRuntimeState.Degraded));

        HandoverProposal proposal =
            Assert.IsType<HandoverProposal>(
                manager.Evaluate());

        Assert.True(
            coordinator.BeginHandover(
                proposal));

        var candidateState =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.B,
                LeftX = 23000,
            };

        Assert.True(
            coordinator.TrySynchronizeCandidate(
                TransportKind.Bluetooth,
                101,
                candidateState));

        // Old link is still authoritative until the synchronized candidate
        // is atomically committed.
        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.Equal(
            TransportKind.Wifi,
            manager.ActiveTransport);

        Assert.True(
            coordinator.TryCommitHandover());

        Assert.Equal(
            TransportKind.Bluetooth,
            session.AuthoritativeTransport);
        Assert.Equal(
            TransportKind.Bluetooth,
            manager.ActiveTransport);
        Assert.Equal(101u, session.LastAcceptedSequence);
        Assert.Equal(candidateState, backend.LastState);

        Assert.DoesNotContain(
            GamepadState.Neutral,
            backend.Submitted);
    }

    [Fact]
    public void CandidateMustResynchronizeIfOldTransportAdvances()
    {
        var clock = new ManualTimeProvider();
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        var manager = new SmartConnectionManager(
            timeProvider: clock);
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive,
            clock);
        var coordinator =
            new SmartHandoverCoordinator(
                manager,
                session,
                safety);

        manager.Report(Snapshot(
            TransportKind.Wifi,
            90));

        coordinator.ActivateInitial(
            Assert.IsType<HandoverProposal>(
                manager.Evaluate()));

        Assert.True(safety.TryAccept(
            TransportKind.Wifi,
            20,
            GamepadState.Neutral));

        var proposal = new HandoverProposal(
            TransportKind.Wifi,
            TransportKind.Bluetooth,
            HandoverReason.ActiveDegraded,
            FailureInduced: true);

        // The manager would normally produce this proposal. For this
        // focused authority test we make Bluetooth active as a candidate
        // only through the coordinator contract.
        manager.Report(Snapshot(
            TransportKind.Bluetooth,
            95));

        Assert.True(
            coordinator.BeginHandover(
                proposal));

        Assert.True(
            coordinator.TrySynchronizeCandidate(
                TransportKind.Bluetooth,
                21,
                GamepadState.Neutral with
                {
                    Buttons = GamepadButtons.B,
                }));

        Assert.True(safety.TryAccept(
            TransportKind.Wifi,
            22,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.X,
            }));

        Assert.False(
            coordinator.TryCommitHandover());

        Assert.True(
            coordinator.TrySynchronizeCandidate(
                TransportKind.Bluetooth,
                23,
                GamepadState.Neutral with
                {
                    Buttons = GamepadButtons.Y,
                }));

        Assert.True(
            coordinator.TryCommitHandover());

        Assert.Equal(
            23u,
            session.LastAcceptedSequence);
        Assert.Equal(
            GamepadButtons.Y,
            backend.LastState.Buttons);
    }

    [Fact]
    public void LosingEveryTransport_NeutralizesAndClearsAuthority()
    {
        var clock = new ManualTimeProvider();
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        var manager = new SmartConnectionManager(
            timeProvider: clock);
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive,
            clock);
        var coordinator =
            new SmartHandoverCoordinator(
                manager,
                session,
                safety);

        manager.Report(Snapshot(
            TransportKind.Wifi,
            90));

        coordinator.ActivateInitial(
            Assert.IsType<HandoverProposal>(
                manager.Evaluate()));

        Assert.True(safety.TryAccept(
            TransportKind.Wifi,
            1,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            }));

        coordinator.LoseAllTransports();

        Assert.Null(session.AuthoritativeTransport);
        Assert.Null(manager.ActiveTransport);
        Assert.Equal(
            GamepadState.Neutral,
            backend.LastState);
    }

    private static TransportHealthSnapshot Snapshot(
        TransportKind transport,
        int score,
        TransportHealthGrade grade =
            TransportHealthGrade.Good,
        TransportRuntimeState state =
            TransportRuntimeState.Ready) =>
        new(
            transport,
            state,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.Zero,
            score,
            grade);

    private sealed class RecordingBackend :
        IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public List<GamepadState> Submitted { get; } =
            new();

        public GamepadState LastState =>
            Submitted.Count == 0
                ? GamepadState.Neutral
                : Submitted[^1];

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Submit(GamepadState state)
        {
            Submitted.Add(state);
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider :
        TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            _timestamp;

        public void Advance(TimeSpan amount)
        {
            _timestamp += amount.Ticks;
        }
    }
}
