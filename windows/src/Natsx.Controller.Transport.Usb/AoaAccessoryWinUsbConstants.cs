namespace Natsx.Controller.Transport.Usb;

public static class AoaAccessoryWinUsbConstants
{
    public static readonly Guid DeviceInterfaceGuid =
        new("4D501A6D-7D41-4F1B-91A8-CC2D5D718C21");

    public static readonly ushort[] SupportedProductIds =
    {
        AndroidOpenAccessoryConstants.AccessoryProductId,
        AndroidOpenAccessoryConstants.AccessoryAdbProductId,
    };

    public static bool IsSupportedProductId(ushort productId) =>
        Array.IndexOf(SupportedProductIds, productId) >= 0;
}
