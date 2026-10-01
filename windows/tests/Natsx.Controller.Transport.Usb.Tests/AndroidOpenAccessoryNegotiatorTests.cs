using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class AndroidOpenAccessoryNegotiatorTests
{
    [Fact]
    public async Task Negotiation_UsesAoaV1VendorRequestSequence()
    {
        var device = new FakeBootstrapDevice(protocolVersion: 2);
        var negotiator = new AndroidOpenAccessoryNegotiator();

        ushort version =
            await negotiator.EnterAccessoryModeAsync(device);

        Assert.Equal((ushort)2, version);
        Assert.Equal(8, device.Transfers.Count);

        UsbControlTransfer getProtocol =
            device.Transfers[0];

        Assert.Equal(
            UsbControlDirection.DeviceToHost,
            getProtocol.Direction);
        Assert.Equal(
            AndroidOpenAccessory.GetProtocolRequest,
            getProtocol.Request);
        Assert.Equal(2, getProtocol.ExpectedLength);

        for (int index = 0; index < 6; index++)
        {
            UsbControlTransfer sendString =
                device.Transfers[index + 1];

            Assert.Equal(
                UsbControlDirection.HostToDevice,
                sendString.Direction);
            Assert.Equal(
                AndroidOpenAccessory.SendStringRequest,
                sendString.Request);
            Assert.Equal(
                (ushort)index,
                sendString.Index);
            Assert.Equal(
                0,
                sendString.Data[^1]);
        }

        UsbControlTransfer start =
            device.Transfers[^1];

        Assert.Equal(
            AndroidOpenAccessory.StartAccessoryRequest,
            start.Request);
        Assert.Equal(
            UsbControlDirection.HostToDevice,
            start.Direction);
        Assert.Empty(start.Data);
    }

    [Fact]
    public async Task Negotiation_RejectsDeviceWithoutAoaV1()
    {
        var device = new FakeBootstrapDevice(protocolVersion: 0);
        var negotiator = new AndroidOpenAccessoryNegotiator();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () =>
                await negotiator.EnterAccessoryModeAsync(device));
    }

    [Fact]
    public void ProductionAccessoryIdentity_IsAccessoryOnlyWithoutAdb()
    {
        Assert.True(
            AndroidOpenAccessory.IsAccessoryIdentity(
                0x18D1,
                0x2D00));

        Assert.False(
            AndroidOpenAccessory.IsAccessoryIdentity(
                0x18D1,
                0x2D01));
    }

    private sealed class FakeBootstrapDevice(
        ushort protocolVersion) : IUsbBootstrapDevice
    {
        public List<UsbControlTransfer> Transfers { get; } = [];

        public ValueTask<byte[]> ControlTransferAsync(
            UsbControlTransfer transfer,
            CancellationToken cancellationToken)
        {
            Transfers.Add(transfer);

            if (transfer.Request ==
                AndroidOpenAccessory.GetProtocolRequest)
            {
                var bytes = new byte[2];

                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes,
                    protocolVersion);

                return ValueTask.FromResult(bytes);
            }

            return ValueTask.FromResult(
                Array.Empty<byte>());
        }
    }
}
