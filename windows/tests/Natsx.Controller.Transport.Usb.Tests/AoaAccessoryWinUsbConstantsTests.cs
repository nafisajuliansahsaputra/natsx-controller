namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class AoaAccessoryWinUsbConstantsTests
{
    [Theory]
    [InlineData(0x2D00, true)]
    [InlineData(0x2D01, true)]
    [InlineData(0x2D02, false)]
    [InlineData(0x2D03, false)]
    [InlineData(0x2D04, false)]
    [InlineData(0x2D05, false)]
    [InlineData(0xFFFF, false)]
    public void SupportedProductIds_AreRestrictedToV1DataTargets(
        ushort productId,
        bool expected)
    {
        Assert.Equal(
            expected,
            AoaAccessoryWinUsbConstants.IsSupportedProductId(productId));
    }

    [Fact]
    public void DeviceInterfaceGuid_IsStable()
    {
        Assert.Equal(
            new Guid("4D501A6D-7D41-4F1B-91A8-CC2D5D718C21"),
            AoaAccessoryWinUsbConstants.DeviceInterfaceGuid);
    }
}
