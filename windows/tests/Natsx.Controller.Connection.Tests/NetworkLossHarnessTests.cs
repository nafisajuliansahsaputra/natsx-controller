using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class NetworkLossHarnessTests
{
    [Fact]
    public async Task ScriptedLoss_FailsOverNeutralizesAndRecoversWithoutBackendRestart()
    {
        var clock = new ManualTimeProvider();
        var backend = new TrackingBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(session, backend, policy, clock);
        var manager = new SmartConnectionManager(policy, clock);

        await backend.StartAsync();

        var wifi = new ScriptedControllerTransport(
            TransportKind.Wifi,
            clock,
            Healthy(TransportKind.Wifi, 95));

        var bluetooth = new ScriptedControllerTransport(
            TransportKind.Bluetooth,
            clock,
            Healthy(TransportKind.Bluetooth, 90));

        await using var runtime = new ControllerTransportRuntime(
            session,
            safety,
            manager,
            new IControllerTransport[] { wifi, bluetooth },
            policy,
            clock);

        GamepadState wifiState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
        };
        GamepadState bluetoothState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.B,
        };

        wifi.Publish(10, wifiState);
        bluetooth.Publish(11, bluetoothState);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Wifi, runtime.ActiveTransport);
        Assert.Equal(wifiState, backend.LastState);

        wifi.SetHealth(Lost(TransportKind.Wifi));
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Bluetooth, runtime.ActiveTransport);
        Assert.Equal(bluetoothState, backend.LastState);
        Assert.False(wifi.IsAuthoritative);
        Assert.True(bluetooth.IsAuthoritative);

        bluetooth.SetHealth(Lost(TransportKind.Bluetooth));
        runtime.EvaluateOnce();

        clock.Advance(policy.NeutralizeSilence + TimeSpan.FromMilliseconds(1));
        runtime.EvaluateOnce();

        Assert.Equal(GamepadState.Neutral, backend.LastState);
        Assert.Equal(1, backend.StartCount);
        Assert.Equal(0, backend.StopCount);

        GamepadState recoveredWifiState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.X,
            LeftX = 2048,
        };

        wifi.SetHealth(Healthy(TransportKind.Wifi, 95));
        wifi.Publish(12, recoveredWifiState);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Bluetooth, runtime.ActiveTransport);

        clock.Advance(policy.NormalWindow + TimeSpan.FromMilliseconds(1));
        wifi.Publish(13, recoveredWifiState);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Wifi, runtime.ActiveTransport);
        Assert.Equal(TransportKind.Wifi, session.AuthoritativeTransport);
        Assert.Equal(recoveredWifiState, backend.LastState);
        Assert.Equal((uint)13, session.LastAcceptedSequence);
        Assert.Equal(1, backend.StartCount);
        Assert.Equal(0, backend.StopCount);
    }

    private static TransportHealthSnapshot Healthy(
        TransportKind kind,
        int score) =>
        new(
            kind,
            TransportRuntimeState.Ready,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.Zero,
            score,
            TransportHealthGrade.Good);

    private static TransportHealthSnapshot Lost(TransportKind kind) =>
        new(
            kind,
            TransportRuntimeState.Failed,
            TimeSpan.Zero,
            TimeSpan.Zero,
            100,
            TimeSpan.FromMilliseconds(121),
            0,
            TransportHealthGrade.Lost);

    private sealed class TrackingBackend : IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public GamepadState? LastState { get; private set; }

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public void Submit(GamepadState state)
        {
            LastState = state;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
