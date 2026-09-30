using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiControllerTransportLifecycleTests
{
    [Fact]
    public async Task FirstAuthenticatedState_PromotesStabilizingTransportToReady()
    {
        byte[] sessionKey = Enumerable.Range(0, WifiTrustedSession.SessionKeySize)
            .Select(static value => (byte)(value + 1))
            .ToArray();

        SessionId sessionId = SessionId.CreateRandom();

        using var trustedSession =
            new WifiTrustedSession(sessionId, sessionKey);

        var receiver = new WifiRealtimeReceiver(trustedSession);
        var lifecycle = new TransportLifecycle();

        await using var transport = new WifiControllerTransport(
            receiver,
            new IPEndPoint(IPAddress.Loopback, 0),
            lifecycle: lifecycle);

        var stateReceived =
            new TaskCompletionSource<TransportGamepadStateEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        transport.GamepadStateReceived += (_, eventArgs) =>
            stateReceived.TrySetResult(eventArgs);

        await transport.ConnectAsync(CancellationToken.None);

        Assert.Equal(
            TransportRuntimeState.Stabilizing,
            transport.State);

        WifiDiagnosticsSnapshot diagnostics =
            transport.GetDiagnosticsSnapshot();

        IPEndPoint localEndPoint =
            Assert.IsType<IPEndPoint>(diagnostics.LocalEndPoint);

        GamepadState state = GamepadState.Neutral with
        {
            Buttons = GamepadButtons.Y,
            LeftY = 4096,
        };

        byte[] datagram = ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                sessionId,
                42,
                987_654,
                GamepadStateCodec.Encode(state)),
            sessionKey);

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sender.SendAsync(
            datagram,
            localEndPoint,
            timeout.Token);

        TransportGamepadStateEventArgs received =
            await stateReceived.Task.WaitAsync(timeout.Token);

        Assert.Equal((uint)42, received.Sequence);
        Assert.Equal(state, received.State);
        Assert.Equal(TransportRuntimeState.Ready, transport.State);

        transport.SetAuthoritative(true);
        Assert.Equal(TransportRuntimeState.Active, transport.State);

        transport.SetAuthoritative(false);
        Assert.Equal(TransportRuntimeState.Ready, transport.State);
    }
}
