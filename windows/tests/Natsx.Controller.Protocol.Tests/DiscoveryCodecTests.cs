using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class DiscoveryCodecTests
{
    [Fact]
    public void Request_RoundTrips()
    {
        SessionId id = SessionId.FromBytes(
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        byte[] bytes = DiscoveryCodec.EncodeRequest(new DiscoveryRequest(id));
        DiscoveryRequest decoded = DiscoveryCodec.DecodeRequest(bytes);

        Assert.Equal(DiscoveryCodec.RequestSize, bytes.Length);
        Assert.Equal(id, decoded.AndroidDeviceId);
    }

    [Fact]
    public void Response_RoundTripsUtf8Name()
    {
        SessionId id = SessionId.FromBytes(
            Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));

        var response = new DiscoveryResponse(id, 37074, "NATSX-LEGION");

        DiscoveryResponse decoded = DiscoveryCodec.DecodeResponse(
            DiscoveryCodec.EncodeResponse(response));

        Assert.Equal(response, decoded);
    }
}
