using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothTrustedHandshakeTests
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
            BluetoothAuthFrameCodec.EncodeChallenge(
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
            new BluetoothTrustedSession(
                sessionId,
                sessionKey))
        {
            remoteReadyFrame =
                BluetoothControlFrameCodec.EncodeSessionReady(
                    androidSession,
                    new SessionReadyPayload(
                        PeerRole.AndroidController,
                        TransportCapabilities.Wifi |
                        TransportCapabilities.Bluetooth,
                        androidPeer),
                    200);
        }

        using var input =
            new MemoryStream(
                Concat(
                    BluetoothStreamFrameCodec.Encode(
                        challengeFrame),
                    BluetoothStreamFrameCodec.Encode(
                        remoteReadyFrame)));

        using var output =
            new MemoryStream();

        var lifecycle =
            new TransportLifecycle(
                TransportRuntimeState.Connecting);

        using var registry =
            new TrustedSessionRegistry();

        var server =
            new BluetoothTrustedHandshakeServer(
                windowsPeer,
                timeProvider:
                    new ManualTimeProvider(),
                lifecycle: lifecycle,
                sessionRegistry: registry);

        using BluetoothTrustedHandshakeCompletion completion =
            await server.AuthenticateAsync(
                input,
                output,
                peer =>
                    peer == androidPeer
                        ? trustSecret.ToArray()
                        : null);

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
            await BluetoothStreamFrameCodec
                .ReadFrameAsync(output);

        var response =
            BluetoothAuthFrameCodec.DecodeResponse(
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
            await BluetoothStreamFrameCodec
                .ReadFrameAsync(output);

        using var verificationSession =
            new BluetoothTrustedSession(
                sessionId,
                sessionKey);

        SessionReadyPayload localReady =
            BluetoothControlFrameCodec.DecodeSessionReady(
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
                TransportCapabilities.Bluetooth));

        CryptographicOperations.ZeroMemory(
            sessionKey);
        CryptographicOperations.ZeroMemory(
            trustSecret);
        CryptographicOperations.ZeroMemory(
            challengeNonce);
    }

    [Fact]
    public async Task Server_RejectsUntrustedPeer()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();

        byte[] challenge =
            BluetoothAuthFrameCodec.EncodeChallenge(
                SessionId.CreateRandom(),
                new AuthChallengePayload(
                    androidPeer,
                    windowsPeer,
                    AuthChallengePayloadCodec
                        .CreateChallenge()),
                100);

        using var input =
            new MemoryStream(
                BluetoothStreamFrameCodec.Encode(
                    challenge));
        using var output =
            new MemoryStream();

        var server =
            new BluetoothTrustedHandshakeServer(
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
