using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbTrustedHandshakeTests
{
    [Fact]
    public async Task Server_AuthenticatesTrustedPeerAndCompletesSessionReady()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();
        byte[] trustSecret =
            Enumerable.Range(1, 32)
                .Select(static value => (byte)value)
                .ToArray();

        SessionId sessionId =
            SessionId.FromBytes(
                Convert.FromHexString(
                    "00112233445566778899AABBCCDDEEFF"));

        byte[] challengeNonce =
            Convert.FromHexString(
                "202122232425262728292A2B2C2D2E2F" +
                "303132333435363738393A3B3C3D3E3F");

        byte[] challengeFrame =
            UsbAuthFrameCodec.EncodeChallenge(
                sessionId,
                new AuthChallengePayload(
                    androidPeer,
                    windowsPeer,
                    challengeNonce.ToArray()),
                100);

        byte[] sessionKey =
            TrustedReconnectCrypto.DeriveSessionKey(
                trustSecret,
                sessionId,
                challengeNonce,
                androidPeer,
                windowsPeer);

        byte[] remoteReadyFrame;

        using (var androidSession =
            new UsbTrustedSession(
                sessionId,
                sessionKey))
        {
            remoteReadyFrame =
                UsbControlFrameCodec.EncodeSessionReady(
                    androidSession,
                    new SessionReadyPayload(
                        PeerRole.AndroidController,
                        TransportCapabilities.Wifi |
                        TransportCapabilities.UsbDirect,
                        androidPeer),
                    200);
        }

        using var input =
            new MemoryStream(
                UsbStreamFrameCodec.Encode(
                    remoteReadyFrame));

        using var output =
            new MemoryStream();

        var lifecycle =
            new TransportLifecycle(
                TransportRuntimeState.Connecting);

        using var registry =
            new TrustedSessionRegistry();

        var server =
            new UsbTrustedHandshakeServer(
                windowsPeer,
                timeProvider:
                    new ManualTimeProvider(),
                lifecycle: lifecycle,
                sessionRegistry: registry);

        using UsbTrustedHandshakeCompletion completion =
            await server.AuthenticateAsync(
                input,
                output,
                peer =>
                    peer == androidPeer
                        ? trustSecret.ToArray()
                        : null,
                challengeFrame);

        Assert.Equal(
            androidPeer,
            completion.RemotePeerId);
        Assert.Equal(
            sessionId,
            completion.Session.SessionId);
        Assert.Equal(
            TransportRuntimeState.Stabilizing,
            lifecycle.State);

        using TrustedSessionMaterial registered =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(androidPeer));

        Assert.Equal(
            sessionId,
            registered.SessionId);

        byte[] registeredKey =
            registered.CopySessionKey();

        Assert.Equal(
            sessionKey,
            registeredKey);

        CryptographicOperations.ZeroMemory(
            registeredKey);

        output.Position = 0;

        byte[] responseFrame =
            await UsbStreamFrameCodec
                .ReadFrameAsync(output);

        var response =
            UsbAuthFrameCodec.DecodeResponse(
                responseFrame);

        Assert.Equal(
            sessionId,
            response.SessionId);
        Assert.Equal(
            windowsPeer,
            response.Payload.ResponderPeerId);
        Assert.Equal(
            androidPeer,
            response.Payload.ChallengerPeerId);

        Assert.True(
            TrustedReconnectCrypto.VerifyProof(
                trustSecret,
                sessionId,
                challengeNonce,
                androidPeer,
                windowsPeer,
                response.Payload.Proof));

        byte[] localReadyFrame =
            await UsbStreamFrameCodec
                .ReadFrameAsync(output);

        using var verificationSession =
            new UsbTrustedSession(
                sessionId,
                sessionKey);

        SessionReadyPayload localReady =
            UsbControlFrameCodec.DecodeSessionReady(
                localReadyFrame,
                verificationSession);

        Assert.Equal(
            PeerRole.WindowsReceiver,
            localReady.Role);
        Assert.Equal(
            windowsPeer,
            localReady.PeerId);
        Assert.True(
            localReady.Capabilities.HasFlag(
                TransportCapabilities.UsbDirect));

        CryptographicOperations.ZeroMemory(
            sessionKey);
        CryptographicOperations.ZeroMemory(
            trustSecret);
        CryptographicOperations.ZeroMemory(
            challengeNonce);
    }

    [Fact]
    public void RumbleRoundTrip_PreservesMotorStrengths()
    {
        byte[] key =
            Enumerable.Range(0, 32)
                .Select(static value => (byte)value)
                .ToArray();

        using var session =
            new UsbTrustedSession(
                SessionId.CreateRandom(),
                key);

        var expected =
            new RumblePayload(
                255,
                33);

        byte[] frame =
            UsbControlFrameCodec
                .EncodeRumble(
                    session,
                    expected,
                    123);

        RumblePayload actual =
            UsbControlFrameCodec
                .DecodeRumble(
                    frame,
                    session);

        Assert.Equal(
            expected,
            actual);
    }

    [Fact]
    public async Task Server_RejectsUntrustedPeer()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();

        byte[] challenge =
            UsbAuthFrameCodec.EncodeChallenge(
                SessionId.CreateRandom(),
                new AuthChallengePayload(
                    androidPeer,
                    windowsPeer,
                    AuthChallengePayloadCodec
                        .CreateChallenge()),
                100);

        using var input =
            new MemoryStream(
                UsbStreamFrameCodec.Encode(
                    challenge));
        using var output =
            new MemoryStream();

        var server =
            new UsbTrustedHandshakeServer(
                windowsPeer);

        await Assert.ThrowsAsync<CryptographicException>(
            async () =>
                await server.AuthenticateAsync(
                    input,
                    output,
                    _ => null));
    }

    private static byte[] Concat(
        byte[] first,
        byte[] second)
    {
        var result =
            new byte[first.Length + second.Length];

        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => 0;
    }
}
