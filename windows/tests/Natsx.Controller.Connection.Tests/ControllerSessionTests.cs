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
    public void BeginNewSession_ResetsSequenceHistory()
    {
        var session = new ControllerSession();
        session.BeginNewSession(TransportKind.Wifi);

        Assert.True(session.TryAccept(TransportKind.Wifi, 500, GamepadState.Neutral));

        session.BeginNewSession(TransportKind.Wifi);

        Assert.Null(session.LastAcceptedSequence);
        Assert.True(session.TryAccept(TransportKind.Wifi, 1, GamepadState.Neutral));
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
}
