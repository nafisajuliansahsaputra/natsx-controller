using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Xunit;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiNetworkLossHarnessTests
{
    [Fact]
    public void LossDuplicateReorderAndReconnect_DoNotStickInputs()
    {
        var harness = new NetworkLossHarness();

        harness.BeginSession();

        var pressed = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            LeftX = 15000,
            RightTrigger = 255,
        };

        Assert.True(harness.Deliver(1, pressed));
        Assert.Equal(pressed, harness.Backend.LastState);

        // Sequence 2 is intentionally dropped.
        var newest = pressed with
        {
            Buttons = GamepadButtons.B,
            LeftX = 22000,
        };

        Assert.True(harness.Deliver(3, newest));

        // Duplicate and late/reordered states may arrive but must never
        // overwrite the already accepted newest full-state snapshot.
        Assert.False(harness.Deliver(3, pressed));
        Assert.False(harness.Deliver(2, pressed));
        Assert.Equal(newest, harness.Backend.LastState);

        harness.Advance(TimeSpan.FromMilliseconds(149));
        Assert.False(harness.EvaluateSafety());
        Assert.Equal(newest, harness.Backend.LastState);

        harness.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(harness.EvaluateSafety());
        Assert.Equal(GamepadState.Neutral, harness.Backend.LastState);
        Assert.True(harness.Backend.IsStarted);

        // A reconnect starts a fresh logical session while keeping the same
        // virtual backend alive. Sequence numbering may restart at 1.
        harness.BeginSession();

        var recovered = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.X,
        };

        Assert.True(harness.Deliver(1, recovered));
        Assert.Equal(recovered, harness.Backend.LastState);
        Assert.True(harness.Backend.IsStarted);
    }

    [Fact]
    public void AllTransportLoss_ClearsAuthorityAndRejectsLatePackets()
    {
        var harness = new NetworkLossHarness();
        harness.BeginSession();

        Assert.True(harness.Deliver(
            10,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            }));

        harness.LoseAllTransports();

        Assert.Equal(GamepadState.Neutral, harness.Backend.LastState);
        Assert.Null(harness.Session.AuthoritativeTransport);

        // A packet from the dead session cannot regain authority by itself.
        Assert.False(harness.Deliver(
            11,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.B,
            }));

        Assert.Equal(GamepadState.Neutral, harness.Backend.LastState);
    }

    private sealed class NetworkLossHarness
    {
        private readonly ManualTimeProvider _clock = new();
        private readonly InputSafetyEngine _safety;
        private readonly WifiGamepadInputRouter _router;

        public NetworkLossHarness()
        {
            Backend = new PersistentBackend();

            Session = new ControllerSession();

            _safety = new InputSafetyEngine(
                Session,
                Backend,
                ConnectionPolicy.Competitive,
                _clock);

            _router = new WifiGamepadInputRouter(_safety);
        }

        public PersistentBackend Backend { get; }

        public ControllerSession Session { get; }

        public void BeginSession()
        {
            Session.BeginNewSession(TransportKind.Wifi);
        }

        public bool Deliver(
            uint sequence,
            GamepadState state)
        {
            return _router.TryHandle(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.GamepadState,
                    FrameFlags.Authenticated,
                    SessionId.FromBytes(
                        Convert.FromHexString(
                            "00112233445566778899AABBCCDDEEFF")),
                    sequence,
                    sequence,
                    GamepadStateCodec.Encode(state)));
        }

        public void Advance(TimeSpan amount)
        {
            _clock.Advance(amount);
        }

        public bool EvaluateSafety()
        {
            return _safety.Evaluate();
        }

        public void LoseAllTransports()
        {
            _safety.LoseAllTransports();
        }
    }

    private sealed class PersistentBackend : IVirtualGamepadBackend
    {
        public bool IsStarted { get; private set; } = true;

        public GamepadState LastState { get; private set; } =
            GamepadState.Neutral;

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(
            CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default)
        {
            IsStarted = false;
            return ValueTask.CompletedTask;
        }

        public void Submit(GamepadState state)
        {
            LastState = state;
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider : TimeProvider
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
