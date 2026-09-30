using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Wifi;
using Xunit;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiGamepadInputRouterTests
{
    [Fact]
    public void AuthenticatedFreshState_ReachesVirtualBackend()
    {
        var backend = new FakeBackend();
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        var router = new WifiGamepadInputRouter(safety);

        var state = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            LeftTrigger = 180,
        };

        bool accepted = router.TryHandle(Frame(10, state));

        Assert.True(accepted);
        Assert.Equal(state, backend.LastState);
        Assert.Equal(10u, session.LastAcceptedSequence);
    }

    [Fact]
    public void DuplicateSequence_IsRejected()
    {
        var backend = new FakeBackend();
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);

        var router = new WifiGamepadInputRouter(safety);

        Assert.True(router.TryHandle(Frame(20, GamepadState.Neutral)));
        Assert.False(router.TryHandle(Frame(20, GamepadState.Neutral)));
        Assert.Equal(1, backend.SubmitCount);
    }

    [Fact]
    public void UnauthenticatedState_IsRejected()
    {
        var backend = new FakeBackend();
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var router = new WifiGamepadInputRouter(
            new InputSafetyEngine(
                session,
                backend,
                ConnectionPolicy.Competitive));

        var frame = Frame(1, GamepadState.Neutral) with
        {
            Flags = FrameFlags.None,
        };

        Assert.False(router.TryHandle(frame));
        Assert.Equal(0, backend.SubmitCount);
    }

    private static ProtocolFrame Frame(uint sequence, GamepadState state) =>
        new(
            ProtocolVersion.Current,
            MessageType.GamepadState,
            FrameFlags.Authenticated,
            SessionId.FromBytes(
                Convert.FromHexString("00112233445566778899AABBCCDDEEFF")),
            sequence,
            sequence,
            GamepadStateCodec.Encode(state));

    private sealed class FakeBackend : IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public int SubmitCount { get; private set; }

        public GamepadState LastState { get; private set; } = GamepadState.Neutral;

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Submit(GamepadState state)
        {
            SubmitCount++;
            LastState = state;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
