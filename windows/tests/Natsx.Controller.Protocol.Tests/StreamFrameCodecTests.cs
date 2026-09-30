using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class StreamFrameCodecTests
{
    [Fact]
    public async Task UnauthenticatedFrame_RoundTrips()
    {
        var frame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.Hello,
            FrameFlags.None,
            SessionId.Zero,
            0,
            42,
            ControlPayloadCodec.EncodeHello(
                new HelloPayload(
                    SessionId.FromBytes(
                        Convert.FromHexString(
                            "00112233445566778899AABBCCDDEEFF")),
                    DeviceRole.AndroidController,
                    TransportMask.Wifi | TransportMask.Bluetooth,
                    CapabilityFlags.WarmStandby,
                    1,
                    1,
                    0,
                    TrustState.Paired)));

        using var stream = new MemoryStream();

        await StreamFrameCodec.WriteAsync(stream, frame);

        stream.Position = 0;

        ProtocolFrame decoded =
            await StreamFrameCodec.ReadAsync(stream);

        Assert.Equal(frame.MessageType, decoded.MessageType);
        Assert.Equal(frame.Payload, decoded.Payload);
    }

    [Fact]
    public async Task AuthenticatedFrame_RoundTrips()
    {
        byte[] key =
            Enumerable.Range(0, 32)
                .Select(value => (byte)value)
                .ToArray();

        var frame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.Heartbeat,
            FrameFlags.Authenticated,
            SessionId.FromBytes(
                Convert.FromHexString(
                    "00112233445566778899AABBCCDDEEFF")),
            0,
            99,
            ControlPayloadCodec.EncodeHeartbeat(
                new HeartbeatPayload(7)));

        using var stream = new MemoryStream();

        await StreamFrameCodec.WriteAsync(
            stream,
            frame,
            key);

        stream.Position = 0;

        ProtocolFrame decoded =
            await StreamFrameCodec.ReadAsync(
                stream,
                key);

        Assert.Equal(frame.MessageType, decoded.MessageType);
        Assert.Equal(frame.SessionId, decoded.SessionId);
    }

    [Fact]
    public async Task InvalidLength_IsRejectedBeforePayloadRead()
    {
        using var stream =
            new MemoryStream(new byte[] { 1, 0 });

        await Assert.ThrowsAsync<FormatException>(
            async () =>
            {
                _ = await StreamFrameCodec.ReadAsync(stream);
            });
    }

    [Fact]
    public async Task TruncatedFrame_ThrowsEndOfStream()
    {
        using var stream =
            new MemoryStream(
                new byte[]
                {
                    44, 0,
                    1, 2, 3,
                });

        await Assert.ThrowsAsync<EndOfStreamException>(
            async () =>
            {
                _ = await StreamFrameCodec.ReadAsync(stream);
            });
    }
}
