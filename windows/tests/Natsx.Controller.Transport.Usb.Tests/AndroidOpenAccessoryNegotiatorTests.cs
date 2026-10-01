using System.Buffers.Binary;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class AndroidOpenAccessoryNegotiatorTests
{
    [Fact]
    public async Task StartAccessoryMode_EmitsCanonicalAoaRequestSequence()
    {
        var device =
            new FakeUsbControlTransferDevice(
                protocolVersion: 2);

        var negotiator =
            new AndroidOpenAccessoryNegotiator();

        ushort protocol =
            await negotiator.StartAccessoryModeAsync(device);

        Assert.Equal((ushort)2, protocol);

        Assert.Equal(
            new[]
            {
                "IN:192:51:0:0:2",
                "OUT:64:52:0:0:NATSX\0",
                "OUT:64:52:0:1:NATSX Controller Windows Receiver\0",
                "OUT:64:52:0:2:Low-latency NATSX Controller USB transport\0",
                "OUT:64:52:0:3:1\0",
                "OUT:64:52:0:4:https://natsx.my.id\0",
                "OUT:64:52:0:5:natsx-controller\0",
                "OUT:64:53:0:0:",
            },
            device.Operations);
    }

    [Fact]
    public async Task StartAccessoryMode_RejectsZeroProtocolVersion()
    {
        var device =
            new FakeUsbControlTransferDevice(
                protocolVersion: 0);

        var negotiator =
            new AndroidOpenAccessoryNegotiator();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () =>
                await negotiator.StartAccessoryModeAsync(device));
    }

    [Theory]
    [InlineData(0x18D1, 0x2D00, true)]
    [InlineData(0x18D1, 0x2D01, true)]
    [InlineData(0x18D1, 0x2D04, true)]
    [InlineData(0x18D1, 0x2D05, true)]
    [InlineData(0x18D1, 0x2D02, false)]
    [InlineData(0x1234, 0x2D00, false)]
    public void HasAccessoryDataInterface_RecognizesCommunicationProducts(
        ushort vendorId,
        ushort productId,
        bool expected)
    {
        Assert.Equal(
            expected,
            AndroidOpenAccessoryConstants.HasAccessoryDataInterface(
                vendorId,
                productId));
    }

    private sealed class FakeUsbControlTransferDevice :
        IUsbControlTransferDevice
    {
        private readonly ushort _protocolVersion;

        public FakeUsbControlTransferDevice(
            ushort protocolVersion)
        {
            _protocolVersion =
                protocolVersion;
        }

        public List<string> Operations { get; } =
            new();

        public ValueTask<int> ControlInAsync(
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.Span,
                _protocolVersion);

            Operations.Add(
                $"IN:{requestType}:{request}:{value}:{index}:{buffer.Length}");

            return ValueTask.FromResult(2);
        }

        public ValueTask ControlOutAsync(
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string payload =
                System.Text.Encoding.UTF8.GetString(
                    data.Span);

            Operations.Add(
                $"OUT:{requestType}:{request}:{value}:{index}:{payload}");

            return ValueTask.CompletedTask;
        }
    }
}
