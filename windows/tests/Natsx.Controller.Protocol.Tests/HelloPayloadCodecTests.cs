namespace Natsx.Controller.Protocol.Tests;

public sealed class HelloPayloadCodecTests
{
    [Fact]
    public void RoundTrip_PreservesDiscoveryFields()
    {
        PeerId peerId = PeerId.FromBytes(
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        var expected = new HelloPayload(
            PeerRole.WindowsReceiver,
            TransportCapabilities.Wifi |
            TransportCapabilities.Bluetooth |
            TransportCapabilities.UsbDirect,
            peerId,
            RealtimePort: 43860,
            DiscoveryNonce: 0x12345678);

        byte[] bytes = HelloPayloadCodec.Encode(expected);
        HelloPayload decoded = HelloPayloadCodec.Decode(bytes);

        Assert.Equal(HelloPayloadCodec.PayloadSize, bytes.Length);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Decode_RejectsUnknownCapabilityBits()
    {
        var bytes = new byte[HelloPayloadCodec.PayloadSize];
        bytes[0] = (byte)PeerRole.AndroidController;
        bytes[1] = 0x80;

        Assert.Throws<FormatException>(
            () => HelloPayloadCodec.Decode(bytes));
    }
}
