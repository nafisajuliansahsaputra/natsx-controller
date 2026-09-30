using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class ControlPayloadCodecTests
{
    [Fact]
    public void Hello_RoundTrips()
    {
        var deviceId = SessionId.FromBytes(
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        var payload = new HelloPayload(
            deviceId,
            DeviceRole.AndroidController,
            TransportMask.Usb | TransportMask.Wifi | TransportMask.Bluetooth,
            CapabilityFlags.Rumble | CapabilityFlags.Competitive240Hz | CapabilityFlags.WarmStandby,
            1,
            1,
            0,
            TrustState.Paired);

        byte[] encoded = ControlPayloadCodec.EncodeHello(payload);
        HelloPayload decoded = ControlPayloadCodec.DecodeHello(encoded);

        Assert.Equal(ControlPayloadCodec.HelloSize, encoded.Length);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Heartbeat_UsesStableLittleEndianEncoding()
    {
        byte[] encoded = ControlPayloadCodec.EncodeHeartbeat(new HeartbeatPayload(0x01020304u));

        Assert.Equal("0403020100000000", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.Equal(0x01020304u, ControlPayloadCodec.DecodeHeartbeat(encoded).ProbeId);
    }

    [Fact]
    public void HandoverPrepare_RoundTrips()
    {
        var payload = new HandoverPreparePayload(
            CandidateTransport: 3,
            HandoverReason.ActiveTransportCriticalOrLost,
            ExpectedNextSequence: 0xAABBCCDDu);

        Assert.Equal(payload, ControlPayloadCodec.DecodeHandoverPrepare(
            ControlPayloadCodec.EncodeHandoverPrepare(payload)));
    }

    [Fact]
    public void ReservedBytes_AreRejected()
    {
        byte[] encoded = ControlPayloadCodec.EncodeHeartbeat(new HeartbeatPayload(1));
        encoded[7] = 1;

        Assert.Throws<FormatException>(() => ControlPayloadCodec.DecodeHeartbeat(encoded));
    }
}
