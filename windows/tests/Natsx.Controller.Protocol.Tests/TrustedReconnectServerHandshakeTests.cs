using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class TrustedReconnectServerHandshakeTests
{
    private static readonly SessionId AndroidId = SessionId.FromBytes(
        Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

    private static readonly SessionId WindowsId = SessionId.FromBytes(
        Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));

    private static readonly SessionId SessionIdVector = SessionId.FromBytes(
        Convert.FromHexString("102132435465768798A9BACBDCEDFE0F"));

    private static readonly byte[] RootKey =
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    private static readonly byte[] Challenge =
        Enumerable.Range(32, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void ValidTrustedReconnect_EstablishesAuthenticatedSession()
    {
        byte[] sessionIdBytes = Convert.FromHexString(
            "102132435465768798A9BACBDCEDFE0F");

        var randomQueue = new Queue<byte[]>(
            new[] { Challenge.ToArray(), sessionIdBytes });

        var windowsHello = new HelloPayload(
            WindowsId,
            DeviceRole.WindowsReceiver,
            TransportMask.Usb | TransportMask.Wifi | TransportMask.Bluetooth,
            CapabilityFlags.Rumble | CapabilityFlags.Guide | CapabilityFlags.WarmStandby,
            1,
            1,
            0,
            TrustState.Paired);

        var server = new TrustedReconnectServerHandshake(
            WindowsId,
            windowsHello,
            deviceId => deviceId == AndroidId
                ? new TrustedPeerCredentials(AndroidId, RootKey.ToArray())
                : null,
            count =>
            {
                byte[] next = randomQueue.Dequeue();
                Assert.Equal(count, next.Length);
                return next;
            },
            () => 1234);

        HelloPayload androidHello = new(
            AndroidId,
            DeviceRole.AndroidController,
            TransportMask.Wifi | TransportMask.Bluetooth,
            CapabilityFlags.Rumble | CapabilityFlags.Competitive240Hz | CapabilityFlags.WarmStandby,
            1,
            1,
            0,
            TrustState.Paired);

        byte[] androidHelloBytes = ControlPayloadCodec.EncodeHello(androidHello);

        var helloFrame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.Hello,
            FrameFlags.None,
            SessionId.Zero,
            0,
            100,
            androidHelloBytes);

        IReadOnlyList<ProtocolFrame> replies = server.HandleHello(helloFrame);

        Assert.Equal(2, replies.Count);
        Assert.Equal(MessageType.Hello, replies[0].MessageType);
        Assert.Equal(MessageType.AuthChallenge, replies[1].MessageType);
        Assert.Equal(SessionIdVector, replies[1].SessionId);

        byte[] windowsHelloBytes = ControlPayloadCodec.EncodeHello(windowsHello);
        byte[] sessionKey = TrustedSessionCrypto.DeriveSessionKey(
            RootKey,
            Challenge,
            AndroidId,
            WindowsId,
            SessionIdVector);

        byte[] proof = TrustedSessionCrypto.ComputeAndroidProof(
            sessionKey,
            androidHelloBytes,
            windowsHelloBytes,
            Challenge,
            SessionIdVector);

        var proofFrame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.AuthResponse,
            FrameFlags.None,
            SessionIdVector,
            0,
            200,
            proof);

        ProtocolFrame ready = server.HandleAuthResponse(proofFrame, currentTransport: 2);

        Assert.Equal(TrustedReconnectServerState.Authenticated, server.State);
        Assert.Equal(MessageType.SessionReady, ready.MessageType);
        Assert.True(ready.Flags.HasFlag(FrameFlags.Authenticated));
        Assert.Equal(SessionIdVector, ready.SessionId);

        byte[] encodedReady = ProtocolFrameCodec.Encode(ready, server.GetSessionKey());
        ProtocolFrame decodedReady = ProtocolFrameCodec.Decode(encodedReady, sessionKey);
        SessionReadyPayload payload = ControlPayloadCodec.DecodeSessionReady(decodedReady.Payload);

        Assert.Equal((byte)2, payload.CurrentTransport);
        Assert.Equal(
            CapabilityFlags.Rumble | CapabilityFlags.WarmStandby,
            payload.NegotiatedCapabilities);
    }

    [Fact]
    public void UnknownPeer_IsRejectedBeforeChallenge()
    {
        var windowsHello = new HelloPayload(
            WindowsId,
            DeviceRole.WindowsReceiver,
            TransportMask.Wifi,
            CapabilityFlags.Rumble,
            1,
            1,
            0,
            TrustState.Paired);

        var server = new TrustedReconnectServerHandshake(
            WindowsId,
            windowsHello,
            _ => null);

        var hello = new HelloPayload(
            AndroidId,
            DeviceRole.AndroidController,
            TransportMask.Wifi,
            CapabilityFlags.Rumble,
            1,
            1,
            0,
            TrustState.Paired);

        Assert.Throws<UnauthorizedAccessException>(() =>
            server.HandleHello(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.Hello,
                    FrameFlags.None,
                    SessionId.Zero,
                    0,
                    0,
                    ControlPayloadCodec.EncodeHello(hello))));
    }
}
