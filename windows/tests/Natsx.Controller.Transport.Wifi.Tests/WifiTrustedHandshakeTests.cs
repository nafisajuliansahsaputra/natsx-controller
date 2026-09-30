using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiTrustedHandshakeTests
{
    [Fact]
    public void TrustedReconnectDerivesInteroperableSession()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret = Enumerable.Range(1, 32)
            .Select(static value => (byte)value)
            .ToArray();

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            trustSecret);

        byte[] challengeDatagram =
            challenger.EncodeChallenge(100_000);

        var responder = new WifiTrustedHandshakeResponder(windowsPeer);

        using WifiTrustedHandshakeResponse response =
            responder.HandleChallenge(
                challengeDatagram,
                trustSecret,
                101_000);

        Assert.Equal(androidPeer, response.RemotePeerId);

        using WifiTrustedSession challengerSession =
            challenger.AcceptResponse(response.ResponseDatagram);

        Assert.Equal(
            challenger.SessionId,
            response.Session.SessionId);
        Assert.Equal(
            challenger.SessionId,
            challengerSession.SessionId);

        var ready = new SessionReadyPayload(
            PeerRole.AndroidController,
            TransportCapabilities.Wifi,
            androidPeer);

        byte[] authenticated =
            WifiControlDatagramCodec.EncodeSessionReady(
                challengerSession,
                ready,
                102_000);

        SessionReadyPayload decoded =
            WifiControlDatagramCodec.DecodeSessionReady(
                authenticated,
                response.Session);

        Assert.Equal(ready, decoded);
    }

    [Fact]
    public void ResponseFromWrongTrustSecretIsRejected()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] expectedSecret = RandomNumberGenerator.GetBytes(32);
        byte[] wrongSecret = RandomNumberGenerator.GetBytes(32);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            windowsPeer,
            expectedSecret);

        var responder = new WifiTrustedHandshakeResponder(windowsPeer);

        using WifiTrustedHandshakeResponse response =
            responder.HandleChallenge(
                challenger.EncodeChallenge(100),
                wrongSecret,
                200);

        Assert.Throws<CryptographicException>(
            () => challenger.AcceptResponse(response.ResponseDatagram));
    }

    [Fact]
    public void ChallengeForDifferentReceiverIsRejected()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId intendedReceiver = PeerId.CreateRandom();
        PeerId actualReceiver = PeerId.CreateRandom();
        byte[] trustSecret = RandomNumberGenerator.GetBytes(32);

        using var challenger = WifiTrustedHandshakeChallenge.Create(
            androidPeer,
            intendedReceiver,
            trustSecret);

        var responder = new WifiTrustedHandshakeResponder(actualReceiver);

        Assert.Throws<CryptographicException>(
            () => responder.HandleChallenge(
                challenger.EncodeChallenge(100),
                trustSecret,
                200));
    }
}
