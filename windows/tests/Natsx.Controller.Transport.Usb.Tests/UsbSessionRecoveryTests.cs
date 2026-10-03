using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbSessionRecoveryTests
{
    [Fact]
    public async Task ReopenedReaderSkipsOldInputUntilCurrentSessionProof()
    {
        PeerId peer = PeerId.CreateRandom();
        SessionId id = SessionId.CreateRandom();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using var session = new UsbTrustedSession(id, key);
        using var registry = new TrustedSessionRegistry();
        registry.Replace(peer, id, key);
        byte[] announcement = Announcement(session, peer);
        using var input = new MemoryStream(Concat(State(session, 100), announcement));
        byte[] first = await UsbInitialSessionReader.ReadAsync(input);
        Assert.Equal(announcement[UsbStreamFrameCodec.LengthPrefixSize..], first);
        var server = new UsbSecondarySessionJoinServer(PeerId.CreateRandom(), registry);
        using var joined = await server.JoinUplinkOnlyAsync(first);
        Assert.Equal(id, joined.Session.SessionId);
        Assert.Equal(peer, joined.RemotePeerId);
    }

    [Fact]
    public async Task ReopenedReaderStillRejectsUnknownSessionProof()
    {
        using var session = new UsbTrustedSession(SessionId.CreateRandom(), RandomNumberGenerator.GetBytes(32));
        using var registry = new TrustedSessionRegistry();
        using var input = new MemoryStream(Concat(State(session, 50), Announcement(session, PeerId.CreateRandom())));
        byte[] first = await UsbInitialSessionReader.ReadAsync(input);
        var server = new UsbSecondarySessionJoinServer(PeerId.CreateRandom(), registry);
        await Assert.ThrowsAsync<CryptographicException>(async () => await server.JoinUplinkOnlyAsync(first));
    }

    [Fact]
    public async Task ReannouncementKeepsInputOrderingAndRejectsWrongSession()
    {
        using var session = new UsbTrustedSession(SessionId.CreateRandom(), RandomNumberGenerator.GetBytes(32));
        using var wrong = new UsbTrustedSession(SessionId.CreateRandom(), RandomNumberGenerator.GetBytes(32));
        PeerId peer = PeerId.CreateRandom();
        byte[] bytes = Concat(Announcement(session, peer), State(session, 7),
            Announcement(wrong, peer), Announcement(session, peer), State(session, 7), State(session, 8));
        using var input = new MemoryStream(bytes);
        await using var receiver = new UsbRealtimeStreamReceiver(session, new TransportLifecycle());
        await receiver.StartAsync(input);
        Assert.Equal(2, receiver.AcceptedControlFrames);
        Assert.Equal(2, receiver.AcceptedFrames);
        Assert.Equal(2, receiver.RejectedFrames);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        UsbGamepadFrame latest = await receiver.States.ReadAsync(timeout.Token);
        Assert.Equal(8u, latest.Sequence);
    }

    private static byte[] Announcement(UsbTrustedSession session, PeerId peer) =>
        UsbStreamFrameCodec.Encode(UsbControlFrameCodec.EncodeSessionReady(session,
            new SessionReadyPayload(PeerRole.AndroidController, TransportCapabilities.UsbDirect, peer), 100));

    private static byte[] State(UsbTrustedSession session, uint sequence) =>
        UsbStreamFrameCodec.Encode(ProtocolFrameCodec.Encode(new ProtocolFrame(
            ProtocolVersion.Current, MessageType.GamepadState, FrameFlags.Authenticated,
            session.SessionId, sequence, 100, GamepadStateCodec.Encode(GamepadState.Neutral)), session.SessionKey));

    private static byte[] Concat(params byte[][] frames) => frames.SelectMany(static frame => frame).ToArray();
}
