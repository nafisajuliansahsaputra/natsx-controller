using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiDiagnosticsTests
{
    [Fact]
    public async Task AcceptedRealtimeDatagram_IsReflectedInDiagnostics()
    {
        byte[] sessionKey = Enumerable.Range(0, WifiTrustedSession.SessionKeySize)
            .Select(static value => (byte)value)
            .ToArray();
        SessionId sessionId = SessionId.CreateRandom();

        using var trustedSession = new WifiTrustedSession(sessionId, sessionKey);
        await using var receiver = new WifiRealtimeReceiver(trustedSession);

        await receiver.StartAsync(
            new IPEndPoint(IPAddress.Loopback, 0));

        IPEndPoint localEndPoint =
            Assert.IsType<IPEndPoint>(receiver.LocalEndPoint);

        GamepadState state = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A,
            RightX = 1234,
        };

        byte[] datagram = ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                sessionId,
                7,
                123_456,
                GamepadStateCodec.Encode(state)),
            sessionKey);

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sender.SendAsync(
            datagram,
            localEndPoint,
            timeout.Token);

        WifiGamepadDatagram received =
            await receiver.States.ReadAsync(timeout.Token);

        Assert.Equal((uint)7, received.Sequence);
        Assert.Equal(state, received.State);

        WifiDiagnosticsSnapshot diagnostics =
            receiver.GetDiagnosticsSnapshot(TransportRuntimeState.Active);

        Assert.True(diagnostics.ReceiverRunning);
        Assert.Equal(TransportRuntimeState.Active, diagnostics.TransportState);
        Assert.NotNull(diagnostics.LocalEndPoint);
        Assert.NotNull(diagnostics.AuthenticatedRemoteEndPoint);
        Assert.Equal(1, diagnostics.AcceptedStateDatagrams);
        Assert.Equal(0, diagnostics.RejectedDatagrams);
        Assert.Equal(0, diagnostics.PacketLossPercent);
        Assert.True(diagnostics.Silence < TimeSpan.FromSeconds(1));
    }
}
