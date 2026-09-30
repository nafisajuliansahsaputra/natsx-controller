using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiRealtimeIngressTests
{
    [Fact]
    public void TryAccept_RejectsWifiUntilItIsAuthoritative()
    {
        var session = new ControllerSession();
        var backend = new FakeBackend();
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);
        var ingress = new WifiRealtimeIngress(safety);

        bool accepted = ingress.TryAccept(
            new WifiGamepadDatagram(
                1,
                100,
                GamepadState.Neutral with { LeftTrigger = 255 }));

        Assert.False(accepted);
        Assert.Equal(1, ingress.RejectedStates);
        Assert.Equal(GamepadState.Neutral, session.CurrentState);
        Assert.Equal(GamepadState.Neutral, backend.LastState);
    }

    [Fact]
    public void TryAccept_SubmitsFreshAuthoritativeWifiStateToBackend()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var backend = new FakeBackend();
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);
        var ingress = new WifiRealtimeIngress(safety);

        GamepadState expected =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
                RightTrigger = 255,
            };

        Assert.True(
            ingress.TryAccept(
                new WifiGamepadDatagram(10, 100, expected)));

        Assert.Equal(expected, session.CurrentState);
        Assert.Equal(expected, backend.LastState);
        Assert.Equal(1, ingress.AcceptedStates);
    }

    [Fact]
    public void TryAccept_RejectsDuplicateAndOutOfOrderState()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        var backend = new FakeBackend();
        var safety = new InputSafetyEngine(
            session,
            backend,
            ConnectionPolicy.Competitive);
        var ingress = new WifiRealtimeIngress(safety);

        Assert.True(
            ingress.TryAccept(
                new WifiGamepadDatagram(
                    100,
                    100,
                    GamepadState.Neutral with { LeftX = 1000 })));

        Assert.False(
            ingress.TryAccept(
                new WifiGamepadDatagram(
                    100,
                    101,
                    GamepadState.Neutral with { LeftX = 2000 })));

        Assert.False(
            ingress.TryAccept(
                new WifiGamepadDatagram(
                    99,
                    102,
                    GamepadState.Neutral with { LeftX = 3000 })));

        Assert.Equal((short)1000, session.CurrentState.LeftX);
        Assert.Equal((short)1000, backend.LastState.LeftX);
        Assert.Equal(2, ingress.RejectedStates);
    }

    private sealed class FakeBackend : IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public GamepadState LastState { get; private set; } =
            GamepadState.Neutral;

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
            LastState = state;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
