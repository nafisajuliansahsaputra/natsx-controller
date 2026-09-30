using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiRealtimeIngressTests
{
    [Fact]
    public void TryAccept_RejectsWifiUntilItIsAuthoritative()
    {
        var controllerSession = new ControllerSession();
        var ingress = new WifiRealtimeIngress(controllerSession);

        bool accepted = ingress.TryAccept(
            new WifiGamepadDatagram(
                1,
                100,
                GamepadState.Neutral with { LeftTrigger = 255 }));

        Assert.False(accepted);
        Assert.Equal(1, ingress.RejectedStates);
        Assert.Equal(GamepadState.Neutral, controllerSession.CurrentState);
    }

    [Fact]
    public void TryAccept_AcceptsFreshAuthoritativeWifiState()
    {
        var controllerSession = new ControllerSession();
        controllerSession.SetAuthoritativeTransport(TransportKind.Wifi);

        var ingress = new WifiRealtimeIngress(controllerSession);
        GamepadState expected =
            GamepadState.Neutral with
            {
                Buttons = GamepadButtons.A,
                RightTrigger = 255,
            };

        Assert.True(
            ingress.TryAccept(
                new WifiGamepadDatagram(10, 100, expected)));

        Assert.Equal(expected, controllerSession.CurrentState);
        Assert.Equal(1, ingress.AcceptedStates);
    }

    [Fact]
    public void TryAccept_RejectsDuplicateAndOutOfOrderState()
    {
        var controllerSession = new ControllerSession();
        controllerSession.SetAuthoritativeTransport(TransportKind.Wifi);

        var ingress = new WifiRealtimeIngress(controllerSession);

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

        Assert.Equal((short)1000, controllerSession.CurrentState.LeftX);
        Assert.Equal(2, ingress.RejectedStates);
    }
}
