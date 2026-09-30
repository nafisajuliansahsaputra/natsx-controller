using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class HandoverSafetyTests
{
    [Fact]
    public void CandidateState_CommitsWithoutNeutralFrame()
    {
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        session.BeginNewSession(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        var wifiState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            LeftX = 12000,
        };

        var bluetoothState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.B,
            LeftX = 18000,
        };

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            100,
            wifiState));

        Assert.True(engine.TryStageHandoverState(
            TransportKind.Bluetooth,
            101,
            bluetoothState));

        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.Equal(1, backend.Submitted.Count);

        Assert.True(engine.TryCommitHandover(
            TransportKind.Bluetooth));

        Assert.Equal(
            TransportKind.Bluetooth,
            session.AuthoritativeTransport);
        Assert.Equal(101u, session.LastAcceptedSequence);
        Assert.Equal(bluetoothState, backend.LastState);
        Assert.Equal(2, backend.Submitted.Count);
        Assert.DoesNotContain(
            GamepadState.Neutral,
            backend.Submitted);
    }

    [Fact]
    public void OldTransportMayAdvanceWhileCandidateStages_AndStaleCommitIsRejected()
    {
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        session.BeginNewSession(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            10,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            }));

        Assert.True(engine.TryStageHandoverState(
            TransportKind.Bluetooth,
            11,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.B,
            }));

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            12,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.X,
            }));

        Assert.False(engine.TryCommitHandover(
            TransportKind.Bluetooth));

        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.Equal(12u, session.LastAcceptedSequence);
        Assert.Equal(GamepadButtons.X, backend.LastState.Buttons);
    }

    [Fact]
    public void CandidateCanResynchronizeAfterStaleStage()
    {
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        session.BeginNewSession(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            50,
            GamepadState.Neutral));

        Assert.True(engine.TryStageHandoverState(
            TransportKind.Bluetooth,
            51,
            GamepadState.Neutral));

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            52,
            GamepadState.Neutral));

        Assert.True(engine.TryStageHandoverState(
            TransportKind.Bluetooth,
            53,
            GamepadState.Neutral with
            {
                RightTrigger = 255,
            }));

        Assert.True(engine.TryCommitHandover(
            TransportKind.Bluetooth));

        Assert.Equal(53u, session.LastAcceptedSequence);
        Assert.Equal(255, backend.LastState.RightTrigger);
    }

    [Fact]
    public void LoseAllTransports_ClearsAuthorityAndNeutralizes()
    {
        var backend = new RecordingBackend();
        var session = new ControllerSession();
        session.BeginNewSession(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            1,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            }));

        engine.LoseAllTransports();

        Assert.Null(session.AuthoritativeTransport);
        Assert.Equal(GamepadState.Neutral, session.CurrentState);
        Assert.Equal(GamepadState.Neutral, backend.LastState);
        Assert.True(engine.IsNeutralized);
    }

    private sealed class RecordingBackend : IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public List<GamepadState> Submitted { get; } = new();

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
}
