using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothSmartAutoIntegrationTests
{
    private static readonly byte[] SessionKey =
        Convert.FromHexString(
            "000102030405060708090A0B0C0D0E0F" +
            "101112131415161718191A1B1C1D1E1F");

    private static readonly SessionId SessionIdValue =
        SessionId.FromBytes(
            Convert.FromHexString(
                "00112233445566778899AABBCCDDEEFF"));

    [Fact]
    public async Task WifiLossUsesWarmBluetoothThenHandsBackAfterStableRecovery()
    {
        var clock = new ManualTimeProvider();

        ConnectionPolicy policy =
            ConnectionPolicy.Competitive with
            {
                FastWindow = TimeSpan.FromMilliseconds(100),
                NormalWindow = TimeSpan.FromMilliseconds(200),
                LongWindow = TimeSpan.FromSeconds(2),
                WifiFailedRecoveryStability =
                    TimeSpan.FromMilliseconds(300),
                WifiDegradedRecoveryStability =
                    TimeSpan.FromMilliseconds(300),
                FailurePenaltyWindow =
                    TimeSpan.FromMilliseconds(300),
                FailureSwitchCooldown =
                    TimeSpan.FromMilliseconds(300),
                NormalSwitchCooldown =
                    TimeSpan.FromMilliseconds(500),
            };

        var backend = new FakeBackend();
        var session = new ControllerSession();
        var safety =
            new InputSafetyEngine(
                session,
                backend,
                policy,
                clock);
        var manager =
            new SmartConnectionManager(
                policy,
                clock);

        var wifi =
            new ScriptedWifiTransport(
                clock,
                HealthyWifi());

        using var bluetoothSession =
            new BluetoothTrustedSession(
                SessionIdValue,
                SessionKey);

        var bluetooth =
            new BluetoothControllerTransport(
                bluetoothSession,
                timeProvider: clock,
                connectionPolicy: policy);

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

        GamepadState wifiInitial =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            };

        wifi.Publish(
            0x01020303u,
            wifiInitial);

        await runtime.StartAsync();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            wifiInitial,
            backend.LastState);

        byte[] bluetoothFrame =
            BluetoothStreamFrameCodec.Encode(
                BuildBluetoothStateFrame(
                    sequence: 0x01020304u,
                    state:
                        GamepadState.Neutral with
                        {
                            Buttons =
                                GamepadButtons.B,
                        }));

        await using var bluetoothInput =
            new BlockingTailStream(
                bluetoothFrame);

        await bluetooth.AttachAuthenticatedStreamAsync(
            bluetoothInput);

        await WaitUntilAsync(
            TimeSpan.FromSeconds(2),
            () =>
                bluetooth.State ==
                TransportRuntimeState.Ready);

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportRuntimeState.Ready,
            bluetooth.State);

        wifi.SetHealth(
            LostWifi());

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Bluetooth,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportRuntimeState.Active,
            bluetooth.State);
        Assert.Equal(
            GamepadButtons.B,
            backend.LastState?.Buttons);

        GamepadState recoveredWifi =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.X,
                LeftX = 2500,
            };

        wifi.SetHealth(
            HealthyWifi());
        wifi.Publish(
            0x01020305u,
            recoveredWifi);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Bluetooth,
            runtime.ActiveTransport);

        clock.Advance(
            TimeSpan.FromMilliseconds(301));

        wifi.SetHealth(
            HealthyWifi());
        wifi.Publish(
            0x01020306u,
            recoveredWifi);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportRuntimeState.Ready,
            bluetooth.State);
        Assert.Equal(
            recoveredWifi,
            backend.LastState);

        // A freshly completed handback must not immediately flap back to the
        // warm Bluetooth transport on a merely degraded Wi-Fi sample.
        wifi.SetHealth(
            DegradedWifi());
        bluetoothFrame =
            BluetoothStreamFrameCodec.Encode(
                BuildBluetoothStateFrame(
                    sequence: 0x01020307u,
                    state:
                        GamepadState.Neutral with
                        {
                            Buttons =
                                GamepadButtons.Y,
                        }));

        // The production Bluetooth receiver remains warm. It does not become
        // authoritative again solely because a newer standby state exists.
        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            ConnectionManagerState.Cooldown,
            runtime.State);
    }

    private static byte[] BuildBluetoothStateFrame(
        uint sequence,
        GamepadState state)
    {
        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                SessionIdValue,
                sequence,
                123_456,
                GamepadStateCodec.Encode(state)),
            SessionKey);
    }

    private static TransportHealthSnapshot HealthyWifi() =>
        new(
            TransportKind.Wifi,
            TransportRuntimeState.Ready,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1),
            0,
            TimeSpan.Zero,
            95,
            TransportHealthGrade.Good);

    private static TransportHealthSnapshot LostWifi() =>
        new(
            TransportKind.Wifi,
            TransportRuntimeState.Failed,
            TimeSpan.Zero,
            TimeSpan.Zero,
            100,
            TimeSpan.FromMilliseconds(121),
            0,
            TransportHealthGrade.Lost);

    private static TransportHealthSnapshot DegradedWifi() =>
        new(
            TransportKind.Wifi,
            TransportRuntimeState.Degraded,
            TimeSpan.FromMilliseconds(45),
            TimeSpan.FromMilliseconds(10),
            4,
            TimeSpan.FromMilliseconds(55),
            55,
            TransportHealthGrade.Degraded);

    private static async Task WaitUntilAsync(
        TimeSpan timeout,
        Func<bool> predicate)
    {
        DateTime deadline =
            DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(
            predicate(),
            "Condition was not met before timeout.");
    }

    private sealed class ScriptedWifiTransport :
        IControllerTransport
    {
        private readonly TimeProvider _clock;

        public ScriptedWifiTransport(
            TimeProvider clock,
            TransportHealthSnapshot snapshot)
        {
            _clock = clock;
            Snapshot = snapshot;
        }

        public event EventHandler<TransportGamepadStateEventArgs>?
            GamepadStateReceived;

        public event EventHandler<TransportRuntimeStateChangedEventArgs>?
            StateChanged;

        public TransportKind Kind =>
            TransportKind.Wifi;

        public TransportRuntimeState State =>
            Snapshot.State;

        public TransportHealthSnapshot Snapshot
        {
            get;
            private set;
        }

        public ValueTask ConnectAsync(
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisconnectAsync(
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void SetAuthoritative(
            bool authoritative)
        {
            if (Snapshot.State is
                TransportRuntimeState.Failed or
                TransportRuntimeState.Unavailable)
            {
                return;
            }

            TransportRuntimeState next =
                authoritative
                    ? TransportRuntimeState.Active
                    : TransportRuntimeState.Ready;

            if (Snapshot.State == next)
            {
                return;
            }

            Snapshot =
                Snapshot with
                {
                    State = next,
                };

            StateChanged?.Invoke(
                this,
                new TransportRuntimeStateChangedEventArgs(
                    TransportKind.Wifi,
                    next));
        }

        public TransportHealthSnapshot
            GetHealthSnapshot() =>
            Snapshot;

        public void SetHealth(
            TransportHealthSnapshot snapshot)
        {
            Snapshot = snapshot;

            StateChanged?.Invoke(
                this,
                new TransportRuntimeStateChangedEventArgs(
                    TransportKind.Wifi,
                    snapshot.State));
        }

        public void Publish(
            uint sequence,
            GamepadState state)
        {
            GamepadStateReceived?.Invoke(
                this,
                new TransportGamepadStateEventArgs(
                    TransportKind.Wifi,
                    sequence,
                    state,
                    _clock.GetTimestamp()));
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeBackend :
        IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public GamepadState? LastState
        {
            get;
            private set;
        }

        public event Action<RumbleState>?
            RumbleReceived
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

        public void Submit(
            GamepadState state)
        {
            LastState = state;
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider :
        TimeProvider
    {
        // Production monotonic clocks are already non-zero by the time a
        // realtime frame arrives. Keep zero reserved for receiver "no sample"
        // sentinels in deterministic tests as well.
        private long _timestamp =
            TimeSpan.FromSeconds(1).Ticks;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            _timestamp;

        public void Advance(
            TimeSpan delta)
        {
            _timestamp += delta.Ticks;
        }
    }

    private sealed class BlockingTailStream :
        Stream
    {
        private readonly byte[] _data;
        private int _position;

        public BlockingTailStream(
            byte[] data)
        {
            _data =
                data.ToArray();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            if (_position >= _data.Length)
            {
                return 0;
            }

            int available =
                Math.Min(
                    count,
                    _data.Length - _position);

            Array.Copy(
                _data,
                _position,
                buffer,
                offset,
                available);

            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _data.Length)
            {
                int available =
                    Math.Min(
                        buffer.Length,
                        _data.Length - _position);

                _data.AsMemory(
                    _position,
                    available).CopyTo(buffer);

                _position += available;

                return ValueTask.FromResult(
                    available);
            }

            return new ValueTask<int>(
                WaitForCancellationAsync(
                    cancellationToken));
        }

        private static async Task<int>
            WaitForCancellationAsync(
                CancellationToken cancellationToken)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(
            long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
