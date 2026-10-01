using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbSmartAutoIntegrationTests
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
    public async Task StableUsbTakesPreferenceThenDisconnectFallsBackToWarmWifi()
    {
        var clock = new ManualTimeProvider();

        ConnectionPolicy policy =
            ConnectionPolicy.Competitive with
            {
                UsbRecoveryStability =
                    TimeSpan.FromMilliseconds(100),
                SuspectSilence =
                    TimeSpan.FromMilliseconds(200),
                DegradedSilence =
                    TimeSpan.FromMilliseconds(300),
                FailoverSilence =
                    TimeSpan.FromMilliseconds(400),
                LostSilence =
                    TimeSpan.FromMilliseconds(500),
                NeutralizeSilence =
                    TimeSpan.FromMilliseconds(600),
                FastWindow =
                    TimeSpan.FromMilliseconds(50),
                NormalWindow =
                    TimeSpan.FromMilliseconds(100),
                LongWindow =
                    TimeSpan.FromSeconds(1),
                NormalSwitchCooldown =
                    TimeSpan.FromMilliseconds(200),
                FailureSwitchCooldown =
                    TimeSpan.FromMilliseconds(200),
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

        using var usbSession =
            new UsbTrustedSession(
                SessionIdValue,
                SessionKey);

        var usb =
            new UsbControllerTransport(
                usbSession,
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
                    usb,
                },
                policy,
                clock);

        GamepadState wifiInitial =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
            };

        wifi.Publish(10, wifiInitial);
        await runtime.StartAsync();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);

        GamepadState usbState =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.B,
                RightX = 3000,
            };

        byte[] usbFrame =
            UsbStreamFrameCodec.Encode(
                BuildUsbStateFrame(
                    sequence: 11,
                    state: usbState));

        await using var usbInput =
            new ControllableTailStream(
                usbFrame);

        await usb.AttachAuthenticatedStreamAsync(
            usbInput);

        await WaitUntilAsync(
            TimeSpan.FromSeconds(2),
            () =>
                usb.State ==
                TransportRuntimeState.Ready);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);

        clock.Advance(
            TimeSpan.FromMilliseconds(101));

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Usb,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportKind.Usb,
            session.AuthoritativeTransport);
        Assert.Equal(
            usbState,
            backend.LastState);

        GamepadState warmWifi =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.X,
                LeftX = 2400,
            };

        wifi.Publish(
            12,
            warmWifi);

        usbInput.Fail();

        await WaitUntilAsync(
            TimeSpan.FromSeconds(2),
            () =>
                usb.State ==
                TransportRuntimeState.Failed);

        runtime.EvaluateOnce();

        Assert.Equal(
            TransportKind.Wifi,
            runtime.ActiveTransport);
        Assert.Equal(
            TransportKind.Wifi,
            session.AuthoritativeTransport);
        Assert.Equal(
            warmWifi,
            backend.LastState);
        Assert.Equal(
            (uint)12,
            session.LastAcceptedSequence);
    }

    private static byte[] BuildUsbStateFrame(
        uint sequence,
        GamepadState state) =>
        ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                SessionIdValue,
                sequence,
                123_456,
                GamepadStateCodec.Encode(state)),
            SessionKey);

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

        public TransportHealthSnapshot GetHealthSnapshot() =>
            Snapshot;

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

    private sealed class ControllableTailStream :
        Stream
    {
        private readonly byte[] _data;
        private readonly TaskCompletionSource<bool> _fail =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public ControllableTailStream(
            byte[] data)
        {
            _data = data.ToArray();
        }

        public void Fail() =>
            _fail.TrySetResult(true);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _data.Length)
            {
                int count =
                    Math.Min(
                        buffer.Length,
                        _data.Length - _position);

                _data.AsMemory(
                    _position,
                    count).CopyTo(buffer);

                _position += count;

                return ValueTask.FromResult(count);
            }

            return new ValueTask<int>(
                WaitForFailureAsync(
                    cancellationToken));
        }

        private async Task<int> WaitForFailureAsync(
            CancellationToken cancellationToken)
        {
            await _fail.Task.WaitAsync(
                cancellationToken);

            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

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
