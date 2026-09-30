namespace Natsx.Controller.Transport.Bluetooth.Tests;

public sealed class BluetoothStreamFrameCodecTests
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
            BluetoothStreamFrameCodec.Encode(frame);

        Assert.Equal(76, frame.Length);
        Assert.Equal(
            "4c00" + CanonicalFrameHex,
            Convert.ToHexString(encoded).ToLowerInvariant());
    }

    [Fact]
    public async Task ReadFrameAsync_RoundTripsOneFramedMessage()
    {
        byte[] frame =
            Convert.FromHexString(CanonicalFrameHex);

        byte[] encoded =
            BluetoothStreamFrameCodec.Encode(frame);

        await using var stream =
            new MemoryStream(encoded);

        byte[] decoded =
            await BluetoothStreamFrameCodec.ReadFrameAsync(stream);

        Assert.Equal(frame, decoded);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(43)]
    [InlineData(4157)]
    public void DecodeLengthPrefix_RejectsOutOfBoundsLength(
        int length)
    {
        byte[] prefix =
        {
            (byte)(length & 0xFF),
            (byte)((length >> 8) & 0xFF),
        };

        Assert.Throws<FormatException>(
            () => BluetoothStreamFrameCodec
                .DecodeLengthPrefix(prefix));
    }
}
