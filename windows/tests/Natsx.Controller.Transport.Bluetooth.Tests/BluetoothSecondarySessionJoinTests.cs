using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothSecondarySessionJoinTests
{
    [Fact]
    public async Task Join_ReusesActiveSessionAndMarksStabilizing()
    {
        PeerId androidPeer =
            PeerId.CreateRandom();
        PeerId windowsPeer =
            PeerId.CreateRandom();
        SessionId sessionId =
            SessionId.CreateRandom();
        byte[] sessionKey =
            RandomNumberGenerator.GetBytes(32);

        using var registry =
            new TrustedSessionRegistry();

        registry.Replace(
            androidPeer,
            sessionId,
            sessionKey);

        byte[] remoteReady;
        byte[] remoteTransportReady;

        using (var androidSession =
            new BluetoothTrustedSession(
                sessionId,
                sessionKey))
        {
            remoteReady =
                BluetoothControlFrameCodec
                    .EncodeSessionReady(
                        androidSession,
                        new SessionReadyPayload(
                            PeerRole.AndroidController,
                            TransportCapabilities.Wifi |
                            TransportCapabilities.Bluetooth,
                            androidPeer),
                        100);

            remoteTransportReady =
                BluetoothControlFrameCodec
                    .EncodeTransportReady(
                        androidSession,
                        ProtocolTransport.Bluetooth,
                        200);
        }

        using var input =
            new MemoryStream(
                Concat(
                    BluetoothStreamFrameCodec.Encode(
                        remoteReady),
                    BluetoothStreamFrameCodec.Encode(
                        remoteTransportReady)));

        using var output =
            new MemoryStream();

        var lifecycle =
            new TransportLifecycle(
                TransportRuntimeState.Connecting);

        var server =
            new BluetoothSecondarySessionJoinServer(
                windowsPeer,
                registry,
                timeProvider:
                    new ManualTimeProvider(),
                lifecycle: lifecycle);

        using BluetoothSecondarySessionJoinCompletion completion =
            await server.JoinAsync(
                input,
                output);

        Assert.Equal(
            androidPeer,
            completion.RemotePeerId);
        Assert.Equal(
            sessionId,
            completion.Session.SessionId);
        Assert.Equal(
            TransportRuntimeState.Stabilizing,
            lifecycle.State);

        output.Position = 0;

        using var verificationSession =
            new BluetoothTrustedSession(
                sessionId,
                sessionKey);

        byte[] localReadyFrame =
            await BluetoothStreamFrameCodec
                .ReadFrameAsync(output);

        SessionReadyPayload localReady =
            BluetoothControlFrameCodec
                .DecodeSessionReady(
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

        byte[] localTransportReadyFrame =
            await BluetoothStreamFrameCodec
                .ReadFrameAsync(output);

        TransportReadyPayload localTransportReady =
            BluetoothControlFrameCodec
                .DecodeTransportReady(
                    localTransportReadyFrame,
                    verificationSession);

        Assert.Equal(
            ProtocolTransport.Bluetooth,
            localTransportReady.Transport);

        CryptographicOperations.ZeroMemory(
            sessionKey);
    }

    [Fact]
    public async Task Join_RejectsUnknownSessionBeforeTrustingPeerPayload()
    {
        PeerId androidPeer =
            PeerId.CreateRandom();
        PeerId windowsPeer =
            PeerId.CreateRandom();
        byte[] sessionKey =
            RandomNumberGenerator.GetBytes(32);

        using var registry =
            new TrustedSessionRegistry();

        using var unrelatedSession =
            new BluetoothTrustedSession(
                SessionId.CreateRandom(),
                sessionKey);

        byte[] ready =
            BluetoothControlFrameCodec
                .EncodeSessionReady(
                    unrelatedSession,
                    new SessionReadyPayload(
                        PeerRole.AndroidController,
                        TransportCapabilities.Bluetooth,
                        androidPeer),
                    100);

        using var input =
            new MemoryStream(
                BluetoothStreamFrameCodec
                    .Encode(ready));
        using var output =
            new MemoryStream();

        var server =
            new BluetoothSecondarySessionJoinServer(
                windowsPeer,
                registry);

        await Assert.ThrowsAsync<CryptographicException>(
            async () =>
                await server.JoinAsync(
                    input,
                    output));

        CryptographicOperations.ZeroMemory(
            sessionKey);
    }

    private static byte[] Concat(
        byte[] first,
        byte[] second)
    {
        var result =
            new byte[
                first.Length +
                second.Length];

        first.CopyTo(
            result,
            0);
        second.CopyTo(
            result,
            first.Length);

        return result;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => 0;
    }
}
