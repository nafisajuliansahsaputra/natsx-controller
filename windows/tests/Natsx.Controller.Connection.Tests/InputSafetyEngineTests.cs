using Natsx.Controller.Core;
using Natsx.Controller.Connection;

namespace Natsx.Controller.Connection.Tests;

public sealed class InputSafetyEngineTests
{
    [Fact]
    public void Evaluate_NeutralizesAfterSafetyTimeout()
    {
        var clock = new ManualTimeProvider();
        var backend = new RecordingVirtualGamepadBackend();
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive,
            clock);

        var active = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            LeftX = 12000,
            RightTrigger = 255,
        };

        Assert.True(engine.TryAccept(TransportKind.Wifi, 1, active));
        Assert.Equal(active, backend.LastState);

        clock.Advance(TimeSpan.FromMilliseconds(149));
        Assert.False(engine.Evaluate());
        Assert.Equal(active, backend.LastState);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(engine.Evaluate());
        Assert.Equal(GamepadState.Neutral, backend.LastState);
        Assert.True(engine.IsNeutralized);
    }

    [Fact]
    public void FreshState_ClearsNeutralizedState()
    {
        var clock = new ManualTimeProvider();
        var backend = new RecordingVirtualGamepadBackend();
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var engine = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive,
            clock);

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            1,
            GamepadState.Neutral with { Buttons = GamepadButtons.A }));

        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(engine.Evaluate());

        Assert.True(engine.TryAccept(
            TransportKind.Wifi,
            2,
            GamepadState.Neutral with { Buttons = GamepadButtons.B }));

        Assert.False(engine.IsNeutralized);
        Assert.Equal(GamepadButtons.B, backend.LastState.Buttons);
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

    private sealed class RecordingVirtualGamepadBackend : IVirtualGamepadBackend
    {
        public bool IsStarted { get; private set; }

        public GamepadState LastState { get; private set; } = GamepadState.Neutral;

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = false;
            return ValueTask.CompletedTask;
        }

        public void Submit(GamepadState state)
        {
            LastState = state;
        }

        public ValueTask DisposeAsync()
        {
            IsStarted = false;
            return ValueTask.CompletedTask;
        }
    }
}
