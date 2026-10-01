namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbStreamFrameCodecTests
{
    private const string CanonicalFrameHex =
        "4e584331010007012800100000112233445566778899aabbccddeeff" +
        "040302010807060504030201290205000080ff7fc7cf393011fa0000" +
        "200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be";

    [Fact]
    public void Encode_CanonicalFrame_AddsLittleEndianLengthPrefix()
    {
        byte[] frame =
            Convert.FromHexString(CanonicalFrameHex);

        byte[] encoded =
            UsbStreamFrameCodec.Encode(frame);

        Assert.Equal(
            "4c00" + CanonicalFrameHex,
            Convert.ToHexString(encoded).ToLowerInvariant());
    }

    [Fact]
    public async Task ReadFrameAsync_RoundTripsOneMessage()
    {
        byte[] frame =
            Convert.FromHexString(CanonicalFrameHex);

        byte[] encoded =
            UsbStreamFrameCodec.Encode(frame);

        await using var stream =
            new MemoryStream(encoded);

        byte[] decoded =
            await UsbStreamFrameCodec.ReadFrameAsync(stream);

        Assert.Equal(frame, decoded);
    }
}
