using System.Net;
using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiTrustedControlProcessorTests
{
    [Fact]
    public void ChallengeAndSessionReadyPromoteTrustedSession()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret = RandomNumberGenerator.GetBytes(32);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 55000);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            trustSecret);

        var lifecycle = new TransportLifecycle(
            TransportRuntimeState.Available);
        using var registry =
            new TrustedSessionRegistry();

        using var processor =
            new WifiTrustedControlProcessor(
                windowsPeer,
                timeProvider: null,
                lifecycle: lifecycle,
                sessionRegistry: registry);

        byte[] authResponse = processor.HandleChallenge(
            challenger.EncodeChallenge(100),
            endpoint,
            peer => peer == androidPeer
                ? trustSecret.ToArray()
                : null,
            200);

        using WifiTrustedSession androidSession =
            challenger.AcceptResponse(authResponse);

        Assert.Equal(1, processor.PendingCount);
        Assert.Equal(
            TransportRuntimeState.Authenticating,
            lifecycle.State);

        byte[] androidReady =
            WifiControlDatagramCodec.EncodeSessionReady(
                androidSession,
                new SessionReadyPayload(
                    PeerRole.AndroidController,
                    TransportCapabilities.Wifi,
                    androidPeer),
                300);

        using WifiTrustedControlCompletion completion =
            processor.HandleSessionReady(
                androidReady,
                endpoint,
                400);

        Assert.Equal(0, processor.PendingCount);
        Assert.Equal(androidPeer, completion.RemotePeerId);
        Assert.Equal(
            TransportRuntimeState.Stabilizing,
            lifecycle.State);

        using TrustedSessionMaterial registered =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(androidPeer));

        Assert.Equal(
            androidSession.SessionId,
            registered.SessionId);

        SessionReadyPayload windowsReady =
            WifiControlDatagramCodec.DecodeSessionReady(
                completion.ResponseDatagram,
                androidSession);

        Assert.Equal(PeerRole.WindowsReceiver, windowsReady.Role);
        Assert.Equal(windowsPeer, windowsReady.PeerId);
        Assert.True(
            windowsReady.Capabilities.HasFlag(
                TransportCapabilities.Wifi));
    }

    [Fact]
    public void ReconnectHandshake_DoesNotDemoteAlreadyActiveTransport()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret = RandomNumberGenerator.GetBytes(32);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 55010);
        var lifecycle = new TransportLifecycle(
            TransportRuntimeState.Active);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            trustSecret);

        using var processor =
            new WifiTrustedControlProcessor(
                windowsPeer,
                timeProvider: null,
                lifecycle: lifecycle);

        byte[] authResponse = processor.HandleChallenge(
            challenger.EncodeChallenge(100),
            endpoint,
            _ => trustSecret.ToArray(),
            200);

        Assert.Equal(
            TransportRuntimeState.Active,
            lifecycle.State);

        using WifiTrustedSession androidSession =
            challenger.AcceptResponse(authResponse);

        byte[] androidReady =
            WifiControlDatagramCodec.EncodeSessionReady(
                androidSession,
                new SessionReadyPayload(
                    PeerRole.AndroidController,
                    TransportCapabilities.Wifi,
                    androidPeer),
                300);

        using WifiTrustedControlCompletion completion =
            processor.HandleSessionReady(
                androidReady,
                endpoint,
                400);

        Assert.Equal(
            TransportRuntimeState.Active,
            lifecycle.State);
    }

    [Fact]
    public void UnknownPeerCannotStartTrustedHandshake()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret = RandomNumberGenerator.GetBytes(32);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 55001);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            trustSecret);

        using var processor =
            new WifiTrustedControlProcessor(windowsPeer);

        Assert.Throws<CryptographicException>(
            () => processor.HandleChallenge(
                challenger.EncodeChallenge(100),
                endpoint,
                _ => null,
                200));

        Assert.Equal(0, processor.PendingCount);
    }

    [Fact]
    public void SessionReadyMustComeFromChallengeEndpoint()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret = RandomNumberGenerator.GetBytes(32);

        var firstEndpoint =
            new IPEndPoint(IPAddress.Loopback, 55002);
        var otherEndpoint =
            new IPEndPoint(IPAddress.Loopback, 55003);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            trustSecret);

        using var processor =
            new WifiTrustedControlProcessor(windowsPeer);

        byte[] authResponse = processor.HandleChallenge(
            challenger.EncodeChallenge(100),
            firstEndpoint,
            _ => trustSecret.ToArray(),
            200);

        using WifiTrustedSession androidSession =
            challenger.AcceptResponse(authResponse);

        byte[] androidReady =
            WifiControlDatagramCodec.EncodeSessionReady(
                androidSession,
                new SessionReadyPayload(
                    PeerRole.AndroidController,
                    TransportCapabilities.Wifi,
                    androidPeer),
                300);

        Assert.Throws<CryptographicException>(
            () => processor.HandleSessionReady(
                androidReady,
                otherEndpoint,
                400));

        Assert.Equal(0, processor.PendingCount);
    }
}
