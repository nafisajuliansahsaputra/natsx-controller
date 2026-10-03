using Natsx.Controller.Core;
using Natsx.Controller.Connection;

namespace Natsx.Controller.Connection.Tests;

public sealed class ControllerSessionTests
{
    [Fact]
    public void TryAccept_RejectsNonAuthoritativeTransport()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        bool accepted = session.TryAccept(
            TransportKind.Bluetooth,
            1,
            GamepadState.Neutral);

        Assert.False(accepted);
        Assert.Null(session.LastAcceptedSequence);
    }

    [Fact]
    public void TryAccept_RejectsStaleAndDuplicateSequences()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        Assert.True(session.TryAccept(TransportKind.Wifi, 10, GamepadState.Neutral));
        Assert.False(session.TryAccept(TransportKind.Wifi, 10, GamepadState.Neutral));
        Assert.False(session.TryAccept(TransportKind.Wifi, 9, GamepadState.Neutral));
        Assert.Equal(10u, session.LastAcceptedSequence);
    }

    [Fact]
    public void TryAccept_PreservesGlobalSequenceAcrossTransportSwitch()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);

        Assert.True(session.TryAccept(TransportKind.Wifi, 100, GamepadState.Neutral));

        session.SetAuthoritativeTransport(TransportKind.Bluetooth);

        Assert.False(session.TryAccept(TransportKind.Bluetooth, 100, GamepadState.Neutral));
        Assert.True(session.TryAccept(TransportKind.Bluetooth, 101, GamepadState.Neutral));
        Assert.Equal(101u, session.LastAcceptedSequence);
    }
    [Fact]
    public void Reset_AcceptsRestartedPhoneSequenceWithoutAcceptingOldPacketsInSameSession()
    {
        var session = new ControllerSession();
        session.SetAuthoritativeTransport(TransportKind.Wifi);
        Assert.True(session.TryAccept(TransportKind.Wifi, 500_000, GamepadState.Neutral));
        Assert.False(session.TryAccept(TransportKind.Wifi, 0, GamepadState.Neutral));
        session.Reset();
        Assert.Null(session.AuthoritativeTransport);
        Assert.Null(session.LastAcceptedSequence);
        session.SetAuthoritativeTransport(TransportKind.Usb);
        var pressed = GamepadState.Neutral with { Buttons = GamepadButtons.A, LeftX = 20000 };
        Assert.True(session.TryAccept(TransportKind.Usb, 0, pressed));
        Assert.Equal(pressed, session.CurrentState);
        Assert.False(session.TryAccept(TransportKind.Usb, 0, GamepadState.Neutral));
        Assert.True(session.TryAccept(TransportKind.Usb, 1, GamepadState.Neutral));
    }
}
