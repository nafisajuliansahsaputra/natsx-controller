using Natsx.Controller.Transport.Wifi;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiDiscoveryProtocolTests
{
    [Fact]
    public void Request_RoundTripMagic_IsRecognized()
    {
        byte[] request = WifiDiscoveryProtocol.CreateRequest();

        Assert.Equal(WifiDiscoveryProtocol.RequestSize, request.Length);
        Assert.True(WifiDiscoveryProtocol.IsRequest(request));
    }

    [Fact]
    public void Response_RoundTrips()
    {
        byte[] receiverId = Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray();

        var response = new WifiDiscoveryResponse(
            42161,
            receiverId,
            "NATSX-LEGION");

        byte[] encoded = WifiDiscoveryProtocol.EncodeResponse(response);
        WifiDiscoveryResponse decoded = WifiDiscoveryProtocol.DecodeResponse(encoded);

        Assert.Equal(response.RealtimePort, decoded.RealtimePort);
        Assert.Equal(response.ReceiverName, decoded.ReceiverName);
        Assert.Equal(response.ReceiverId, decoded.ReceiverId);
    }

    [Fact]
    public void Response_WithCorruptMagic_IsRejected()
    {
        var response = new WifiDiscoveryResponse(
            42161,
            new byte[16],
            "Receiver");

        byte[] encoded = WifiDiscoveryProtocol.EncodeResponse(response);
        encoded[0] ^= 0xFF;

        Assert.Throws<FormatException>(() =>
            WifiDiscoveryProtocol.DecodeResponse(encoded));
    }
}
