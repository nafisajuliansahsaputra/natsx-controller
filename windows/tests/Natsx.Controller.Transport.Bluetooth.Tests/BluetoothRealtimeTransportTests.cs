using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothRealtimeTransportTests
{
    private static readonly byte[] SessionKey =
        Convert.FromHexString(
            "000102030405060708090A0B0C0D0E0F" +
            "101112131415161718191A1B1C1D1E1F");

    private static readonly SessionId CanonicalSessionId =
        SessionId.FromBytes(
            Convert.FromHexString(
                "00112233445566778899AABBCCDDEEFF"));

    private const string CanonicalFrameHex =
        "4e584331010007012800100000112233445566778899aabbccddeeff" +
        "040302010807060504030201290205000080ff7fc7cf393011fa0000" +
        "200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be";

    [Fact]
    public void DecodeGamepadState_ValidatesCanonicalAuthenticatedFrame()
    {
        using var session =
            new BluetoothTrustedSession(
                CanonicalSessionId,
                SessionKey);

        BluetoothGamepadFrame decoded =
            BluetoothRealtimeFrameCodec.DecodeGamepadState(
                Convert.FromHexString(CanonicalFrameHex),
                session);

        Assert.Equal(0x01020304u, decoded.Sequence);
        Assert.True(
            decoded.State.Buttons.HasFlag(
                GamepadButtons.A));
        Assert.True(
            decoded.State.Buttons.HasFlag(
                GamepadButtons.Y));
        Assert.Equal(
            short.MinValue,
            decoded.State.LeftX);
        Assert.Equal(
            (byte)250,
            decoded.State.RightTrigger);
    }

    [Fact]
    public async Task ControllerTransport_FirstAuthenticatedStateBecomesReady()
    {
        using var session =
            new BluetoothTrustedSession(
                CanonicalSessionId,
                SessionKey);

        await using var transport =
            new BluetoothControllerTransport(session);

        var states =
            new List<TransportRuntimeState>();

        transport.StateChanged += (_, eventArgs) =>
            states.Add(eventArgs.State);

        var received =
            new TaskCompletionSource<TransportGamepadStateEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        transport.GamepadStateReceived += (_, eventArgs) =>
            received.TrySetResult(eventArgs);

        await transport.ConnectAsync(
            CancellationToken.None);

        Assert.Equal(
            TransportRuntimeState.Available,
            transport.State);

        byte[] framed =
            BluetoothStreamFrameCodec.Encode(
                Convert.FromHexString(
                    CanonicalFrameHex));

        await using var input =
            new BlockingTailStream(framed);

        await transport.AttachAuthenticatedStreamAsync(
            input,
            CancellationToken.None);

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(5));

        TransportGamepadStateEventArgs eventArgs =
            await received.Task.WaitAsync(
                timeout.Token);

        Assert.Equal(
            TransportKind.Bluetooth,
            eventArgs.Transport);
        Assert.Equal(
            0x01020304u,
            eventArgs.Sequence);
        Assert.Equal(
            TransportRuntimeState.Ready,
            transport.State);

        Assert.Contains(
            TransportRuntimeState.Available,
            states);
        Assert.Contains(
            TransportRuntimeState.Stabilizing,
            states);
        Assert.Contains(
            TransportRuntimeState.Ready,
            states);

        transport.SetAuthoritative(true);

        Assert.Equal(
            TransportRuntimeState.Active,
            transport.State);

        transport.SetAuthoritative(false);

        Assert.Equal(
            TransportRuntimeState.Ready,
            transport.State);
    }

    [Fact]
    public void DecodeGamepadState_RejectsDifferentSession()
    {
        using var session =
            new BluetoothTrustedSession(
                SessionId.CreateRandom(),
                SessionKey);

        Assert.ThrowsAny<Exception>(
            () => BluetoothRealtimeFrameCodec
                .DecodeGamepadState(
                    Convert.FromHexString(
                        CanonicalFrameHex),
                    session));
    }

    private sealed class BlockingTailStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public BlockingTailStream(byte[] data)
        {
            _data =
                data?.ToArray() ??
                throw new ArgumentNullException(
                    nameof(data));
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            _data.Length;

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
