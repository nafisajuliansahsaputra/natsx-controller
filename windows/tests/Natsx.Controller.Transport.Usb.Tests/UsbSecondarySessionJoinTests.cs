using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbSecondarySessionJoinTests
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
            new UsbTrustedSession(
                sessionId,
                sessionKey))
        {
            remoteReady =
                UsbControlFrameCodec
                    .EncodeSessionReady(
                        androidSession,
                        new SessionReadyPayload(
                            PeerRole.AndroidController,
                            TransportCapabilities.Wifi |
                            TransportCapabilities.UsbDirect,
                            androidPeer),
                        100);

            remoteTransportReady =
                UsbControlFrameCodec
                    .EncodeTransportReady(
                        androidSession,
                        200);
        }

        using var input =
            new MemoryStream(
                Concat(
                    UsbStreamFrameCodec.Encode(
                        remoteReady),
                    UsbStreamFrameCodec.Encode(
                        remoteTransportReady)));

        using var output =
            new MemoryStream();

        var lifecycle =
            new TransportLifecycle(
                TransportRuntimeState.Connecting);

        var server =
            new UsbSecondarySessionJoinServer(
                windowsPeer,
                registry,
                timeProvider:
                    new ManualTimeProvider(),
                lifecycle: lifecycle);

        using UsbSecondarySessionJoinCompletion completion =
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
            new UsbTrustedSession(
                sessionId,
                sessionKey);

        SessionReadyPayload localReady =
            UsbControlFrameCodec
                .DecodeSessionReady(
                    await UsbStreamFrameCodec
                        .ReadFrameAsync(output),
                    verificationSession);

        Assert.Equal(
            PeerRole.WindowsReceiver,
            localReady.Role);
        Assert.True(
            localReady.Capabilities.HasFlag(
                TransportCapabilities.UsbDirect));

        TransportReadyPayload localTransportReady =
            UsbControlFrameCodec
                .DecodeTransportReady(
                    await UsbStreamFrameCodec
                        .ReadFrameAsync(output),
                    verificationSession);

        Assert.Equal(
            ProtocolTransport.UsbDirect,
            localTransportReady.Transport);

        CryptographicOperations.ZeroMemory(
            sessionKey);
    }

    [Fact]
    public async Task Join_RejectsUnknownSession()
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
            new UsbTrustedSession(
                SessionId.CreateRandom(),
                sessionKey);

        byte[] ready =
            UsbControlFrameCodec
                .EncodeSessionReady(
                    unrelatedSession,
                    new SessionReadyPayload(
                        PeerRole.AndroidController,
                        TransportCapabilities.UsbDirect,
                        androidPeer),
                    100);

        using var input =
            new MemoryStream(
                UsbStreamFrameCodec.Encode(
                    ready));
        using var output =
            new MemoryStream();

        var server =
            new UsbSecondarySessionJoinServer(
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

        first.CopyTo(result, 0);
        second.CopyTo(
            result,
            first.Length);

        return result;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            TimeSpan.FromSeconds(1).Ticks;
    }
}
