using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbStreamFrameCodecTests
{
    [Fact]
    public async Task AuthenticatedProtocolFrame_RoundTrips()
    {
        SessionId sessionId =
            SessionId.FromBytes(
                Convert.FromHexString(
                    "00112233445566778899AABBCCDDEEFF"));

        byte[] key =
            Convert.FromHexString(
                "000102030405060708090A0B0C0D0E0F" +
                "101112131415161718191A1B1C1D1E1F");

        byte[] frame =
            ProtocolFrameCodec.Encode(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.Heartbeat,
                    FrameFlags.Authenticated,
                    sessionId,
                    0,
                    123,
                    []),
                key);

        byte[] framed =
            UsbStreamFrameCodec.Encode(frame);

        await using var stream =
            new MemoryStream(framed);

        byte[] decoded =
            await UsbStreamFrameCodec
                .ReadFrameAsync(stream);

        Assert.Equal(frame, decoded);
    }

    [Fact]
    public void OversizedFrame_IsRejected()
    {
        byte[] oversized =
            new byte[
                UsbStreamFrameCodec.MaximumFrameSize + 1];

        Assert.Throws<InvalidDataException>(
            () =>
                UsbStreamFrameCodec.Encode(
                    oversized));
    }
}
