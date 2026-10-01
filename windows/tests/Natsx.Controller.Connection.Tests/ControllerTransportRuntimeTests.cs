using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class ControllerTransportRuntimeTests
{
    [Fact]
    public async Task LifecycleEvents_UpdateManagerStateWithoutPollingTick()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(
            session,
            backend,
            policy,
            clock);
        var manager = new SmartConnectionManager(policy, clock);

        var wifi = new ScriptedControllerTransport(
            TransportKind.Wifi,
            clock,
            new TransportHealthSnapshot(
                TransportKind.Wifi,
                TransportRuntimeState.Available,
                TimeSpan.Zero,
                TimeSpan.Zero,
                0,
                TimeSpan.Zero,
                0,
                TransportHealthGrade.Warning));

        await using var runtime = new ControllerTransportRuntime(
            session,
            safety,
            manager,
            new IControllerTransport[] { wifi },
            policy,
            clock);

        wifi.SetHealth(new TransportHealthSnapshot(
            TransportKind.Wifi,
            TransportRuntimeState.Connecting,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            TimeSpan.Zero,
            0,
            TransportHealthGrade.Warning));

        Assert.Equal(
            ConnectionManagerState.Connecting,
            runtime.State);

        wifi.SetHealth(new TransportHealthSnapshot(
            TransportKind.Wifi,
            TransportRuntimeState.Authenticating,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            TimeSpan.Zero,
            0,
            TransportHealthGrade.Warning));

        Assert.Equal(
            ConnectionManagerState.Authenticating,
            runtime.State);

        wifi.SetHealth(new TransportHealthSnapshot(
            TransportKind.Wifi,
            TransportRuntimeState.Stabilizing,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            TimeSpan.MaxValue,
            0,
            TransportHealthGrade.Lost));

        Assert.Equal(
            ConnectionManagerState.Stabilizing,
            runtime.State);
    }

    [Fact]
    public async Task InitialSelection_SubmitsFreshCandidateAndSetsAuthority()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        var policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(session, backend, policy, clock);
        var manager = new SmartConnectionManager(policy, clock);
        var wifi = new FakeTransport(TransportKind.Wifi, clock)
        {
            Snapshot = Healthy(TransportKind.Wifi, 95),
        };

        await using var runtime = new ControllerTransportRuntime(
            session,
            safety,
            manager,
            new[] { wifi },
            policy,
            clock);

        GamepadState state = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            LeftX = 1234,
        };

        wifi.Publish(10, state);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Wifi, runtime.ActiveTransport);
        Assert.Equal(TransportKind.Wifi, session.AuthoritativeTransport);
        Assert.True(wifi.IsAuthoritative);
        Assert.Equal(TransportRuntimeState.Active, wifi.State);
        Assert.Equal((uint)10, session.LastAcceptedSequence);
        Assert.Equal(state, backend.LastState);
    }

    [Fact]
    public async Task Handover_RejectsStaleCandidateThenAcceptsNewerState()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        var policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(session, backend, policy, clock);
        var manager = new SmartConnectionManager(policy, clock);

        var wifi = new FakeTransport(TransportKind.Wifi, clock)
        {
            Snapshot = Healthy(TransportKind.Wifi, 95),
        };
        var bluetooth = new FakeTransport(TransportKind.Bluetooth, clock)
        {
            Snapshot = Healthy(TransportKind.Bluetooth, 90),
        };

        await using var runtime = new ControllerTransportRuntime(
            session,
            safety,
            manager,
            new IControllerTransport[] { wifi, bluetooth },
            policy,
            clock);

        GamepadState wifiState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.X,
        };

        wifi.Publish(10, wifiState);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Wifi, runtime.ActiveTransport);

        wifi.Snapshot = Critical(TransportKind.Wifi);
        bluetooth.Publish(9, GamepadState.Neutral with
        {
            Buttons = GamepadButtons.B,
        });

        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Wifi, runtime.ActiveTransport);
        Assert.Equal((uint)10, session.LastAcceptedSequence);
        Assert.Equal(wifiState, backend.LastState);

        GamepadState bluetoothState = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.B,
        };

        bluetooth.Publish(11, bluetoothState);
        runtime.EvaluateOnce();

        Assert.Equal(TransportKind.Bluetooth, runtime.ActiveTransport);
        Assert.Equal(TransportKind.Bluetooth, session.AuthoritativeTransport);
        Assert.False(wifi.IsAuthoritative);
        Assert.Equal(TransportRuntimeState.Ready, wifi.State);
        Assert.True(bluetooth.IsAuthoritative);
        Assert.Equal(TransportRuntimeState.Active, bluetooth.State);
        Assert.Equal((uint)11, session.LastAcceptedSequence);
        Assert.Equal(bluetoothState, backend.LastState);
    }

    [Fact]
    public async Task AttachTransportAfterStart_CanBecomeAuthoritative()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(
            session,
            backend,
            policy,
            clock);
        var manager = new SmartConnectionManager(
            policy,
            clock);

        await using var runtime =
            new ControllerTransportRuntime(
                session,
                safety,
                manager,
                Array.Empty<IControllerTransport>(),
                policy,
                clock);

        await runtime.StartAsync();

        var wifi =
            new FakeTransport(
                TransportKind.Wifi,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Wifi,
                        95),
            };

        await runtime.AttachTransportAsync(
            wifi);

        GamepadState state =
            GamepadState.Neutral with
            {
                Buttons =
                    GamepadButtons.A |
                    GamepadButtons.RightShoulder,
                RightX = 4321,
            };

        wifi.Publish(
            1,
            state);
        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.True(
            wifi.IsAuthoritative);
        Assert.Equal(
            state,
            backend.LastState);
    }

    [Fact]
    public async Task DetachActiveTransport_FailsOverToFreshReadyBackup()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(
            session,
            backend,
            policy,
            clock);
        var manager = new SmartConnectionManager(
            policy,
            clock);

        var wifi =
            new FakeTransport(
                TransportKind.Wifi,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Wifi,
                        95),
            };
        var bluetooth =
            new FakeTransport(
                TransportKind.Bluetooth,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Bluetooth,
                        90),
            };

        await using var runtime =
            new ControllerTransportRuntime(
                session,
                safety,
                manager,
                new IControllerTransport[]
                {
                    wifi,
                    bluetooth,
                },
                policy,
                clock);

        await runtime.StartAsync();

        wifi.Publish(
            10,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.X,
            });
        bluetooth.Publish(
            11,
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.B,
            });

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);

        bool removed =
            await runtime.DetachTransportAsync(
                TransportKind.Wifi);

        Assert.True(removed);
        Assert.Equal(
            TransportKind.Bluetooth,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportKind.Bluetooth,
            session.AuthoritativeTransport);
        Assert.True(
            bluetooth.IsAuthoritative);
        Assert.Equal(
            (uint)11,
            session.LastAcceptedSequence);
        Assert.Equal(
            GamepadButtons.B,
            backend.LastState?.Buttons);
    }

    [Fact]
    public async Task DetachActiveTransport_FailsOverWithEquivalentGlobalSequence()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(
            session,
            backend,
            policy,
            clock);
        var manager = new SmartConnectionManager(
            policy,
            clock);

        var usb =
            new FakeTransport(
                TransportKind.Usb,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Usb,
                        98),
            };

        var wifi =
            new FakeTransport(
                TransportKind.Wifi,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Wifi,
                        95),
            };

        await using var runtime =
            new ControllerTransportRuntime(
                session,
                safety,
                manager,
                new IControllerTransport[]
                {
                    usb,
                    wifi,
                },
                policy,
                clock);

        await runtime.StartAsync();

        GamepadState sharedState =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
                LeftX = 3210,
            };

        usb.Publish(
            42,
            sharedState);
        wifi.Publish(
            42,
            sharedState);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Usb,
            runtime.ActiveTransport);

        bool removed =
            await runtime.DetachTransportAsync(
                TransportKind.Usb);

        Assert.True(removed);
        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.Equal(
            (uint)42,
            session.LastAcceptedSequence);
        Assert.Equal(
            sharedState,
            backend.LastState);
    }

    [Fact]
    public async Task RepeatedUsbAttachDetach_PreservesWifiFallbackAndBackend()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        ConnectionPolicy policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(
            session,
            backend,
            policy,
            clock);
        var manager = new SmartConnectionManager(
            policy,
            clock);

        var wifi =
            new FakeTransport(
                TransportKind.Wifi,
                clock)
            {
                Snapshot =
                    Healthy(
                        TransportKind.Wifi,
                        95),
            };

        await using var runtime =
            new ControllerTransportRuntime(
                session,
                safety,
                manager,
                new IControllerTransport[]
                {
                    wifi,
                },
                policy,
                clock);

        await runtime.StartAsync();

        uint sequence = 1;

        wifi.Publish(
            sequence,
            GamepadState.Neutral);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var usb =
                new FakeTransport(
                    TransportKind.Usb,
                    clock)
                {
                    Snapshot =
                        Healthy(
                            TransportKind.Usb,
                            100),
                };

            await runtime.AttachTransportAsync(
                usb);

            sequence += 1;

            GamepadState state =
                GamepadState.Neutral with
                {
                    Buttons =
                        cycle % 2 == 0
                            ? GamepadButtons.A
                            : GamepadButtons.B,
                    LeftX =
                        (short)(1000 + cycle),
                };

            wifi.Publish(
                sequence,
                state);
            usb.Publish(
                sequence,
                state);

            // Keep both warm transports fed with the same session-global
            // revision while USB satisfies its stabilization window.
            TimeSpan elapsed =
                TimeSpan.Zero;

            while (elapsed <
                policy.UsbRecoveryStability)
            {
                TimeSpan step =
                    TimeSpan.FromMilliseconds(
                        50);

                clock.Advance(step);
                elapsed += step;

                wifi.Publish(
                    sequence,
                    state);
                usb.Publish(
                    sequence,
                    state);

                runtime.EvaluateOnce();
            }

            Assert.Equal(
                TransportKind.Usb,
                runtime.ActiveTransport);

            bool removed =
                await runtime.DetachTransportAsync(
                    TransportKind.Usb);

            Assert.True(
                removed);
            Assert.Equal(
                TransportKind.Wifi,
                runtime.ActiveTransport);
            Assert.Equal(
                TransportKind.Wifi,
                session.AuthoritativeTransport);
            Assert.Equal(
                state,
                backend.LastState);
            Assert.True(
                backend.IsStarted);

            clock.Advance(
                TimeSpan.FromMilliseconds(
                    100));
        }
    }

    [Fact]
    public async Task NonAuthoritativeRealtimeState_DoesNotReachBackend()
    {
        var clock = new ManualTimeProvider();
        var backend = new FakeBackend();
        var session = new ControllerSession();
        var policy = ConnectionPolicy.Competitive;
        var safety = new InputSafetyEngine(session, backend, policy, clock);
        var manager = new SmartConnectionManager(policy, clock);

        var wifi = new FakeTransport(TransportKind.Wifi, clock)
        {
            Snapshot = Healthy(TransportKind.Wifi, 95),
        };
        var bluetooth = new FakeTransport(TransportKind.Bluetooth, clock)
        {
            Snapshot = Healthy(TransportKind.Bluetooth, 80),
        };

        await using var runtime = new ControllerTransportRuntime(
            session,
            safety,
            manager,
            new IControllerTransport[] { wifi, bluetooth },
            policy,
            clock);

        wifi.Publish(1, GamepadState.Neutral with { Buttons = GamepadButtons.A });
        runtime.EvaluateOnce();

        GamepadState accepted = Assert.IsType<GamepadState>(backend.LastState);

        bluetooth.Publish(2, GamepadState.Neutral with { Buttons = GamepadButtons.Y });

        Assert.Equal(accepted, backend.LastState);
        Assert.Equal((uint)1, session.LastAcceptedSequence);
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

    private static TransportHealthSnapshot Critical(TransportKind kind) =>
        new(
            kind,
            TransportRuntimeState.Degraded,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(30),
            10,
            TimeSpan.FromMilliseconds(90),
            20,
            TransportHealthGrade.Critical);

    private sealed class FakeTransport : IControllerTransport
    {
        private readonly TimeProvider _clock;

        public FakeTransport(TransportKind kind, TimeProvider clock)
        {
            Kind = kind;
            _clock = clock;
            Snapshot = Healthy(kind, 80);
        }

        public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

        public event EventHandler<TransportRuntimeStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public TransportKind Kind { get; }

        public TransportRuntimeState State => Snapshot.State;

        public TransportHealthSnapshot Snapshot { get; set; }

        public bool IsAuthoritative { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisconnectAsync(
            CancellationToken cancellationToken)
        {
            Snapshot =
                Snapshot with
                {
                    State = TransportRuntimeState.Unavailable,
                    Silence = TimeSpan.MaxValue,
                    Score = 0,
                    Grade = TransportHealthGrade.Lost,
                };

            return ValueTask.CompletedTask;
        }

        public void SetAuthoritative(bool authoritative)
        {
            IsAuthoritative = authoritative;

            if (Snapshot.State is
                TransportRuntimeState.Failed or
                TransportRuntimeState.Unavailable)
            {
                return;
            }

            Snapshot = Snapshot with
            {
                State = authoritative
                    ? TransportRuntimeState.Active
                    : TransportRuntimeState.Ready,
            };
        }

        public TransportHealthSnapshot GetHealthSnapshot() => Snapshot;

        public void Publish(uint sequence, GamepadState state)
        {
            GamepadStateReceived?.Invoke(
                this,
                new TransportGamepadStateEventArgs(
                    Kind,
                    sequence,
                    state,
                    _clock.GetTimestamp()));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBackend : IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public GamepadState? LastState { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

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
