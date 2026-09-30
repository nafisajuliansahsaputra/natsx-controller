using Natsx.Controller.Protocol;
using Natsx.Controller.Transport.Wifi;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiTransportOptionsTests
{
    [Fact]
    public void Constructor_CopiesSensitiveBuffers()
    {
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        byte[] receiverId = Enumerable.Range(0, 16).Select(static value => (byte)(value + 1)).ToArray();

        var options = new WifiTransportOptions(
            new SessionId(1, 2),
            key,
            receiverId,
            "Receiver");

        key[0] = 0xFF;
        receiverId[0] = 0xFF;

        Assert.NotEqual(key[0], options.AuthenticationKey[0]);
        Assert.NotEqual(receiverId[0], options.ReceiverId[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Constructor_RejectsInvalidPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WifiTransportOptions(
                new SessionId(1, 2),
                new byte[32],
                new byte[16],
                "Receiver",
                discoveryPort: port));
    }
}
