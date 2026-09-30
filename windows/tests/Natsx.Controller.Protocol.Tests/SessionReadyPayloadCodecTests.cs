using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class SessionReadyPayloadCodecTests
{
    [Fact]
    public void RoundTripPreservesSessionReadyIdentityAndCapabilities()
    {
        var peerId = PeerId.FromBytes(
            Enumerable.Range(1, PeerId.Size)
                .Select(static value => (byte)value)
                .ToArray());

        var expected = new SessionReadyPayload(
            PeerRole.AndroidController,
            TransportCapabilities.Wifi | TransportCapabilities.Bluetooth,
            peerId);

        byte[] encoded = SessionReadyPayloadCodec.Encode(expected);
        SessionReadyPayload decoded = SessionReadyPayloadCodec.Decode(encoded);

        Assert.Equal(SessionReadyPayloadCodec.PayloadSize, encoded.Length);
        Assert.Equal(expected, decoded);
        Assert.Equal(0, encoded[2]);
        Assert.Equal(0, encoded[3]);
    }

    [Fact]
    public void DecodeRejectsNonZeroReservedBytes()
    {
        var peerId = PeerId.FromBytes(new byte[PeerId.Size]);
        byte[] encoded = SessionReadyPayloadCodec.Encode(
            new SessionReadyPayload(
                PeerRole.WindowsReceiver,
                TransportCapabilities.Wifi,
                peerId));

        encoded[2] = 1;

        Assert.Throws<FormatException>(
            () => SessionReadyPayloadCodec.Decode(encoded));
    }
}
