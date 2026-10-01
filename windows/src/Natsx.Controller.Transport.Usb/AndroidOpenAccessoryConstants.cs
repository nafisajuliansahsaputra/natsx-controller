namespace Natsx.Controller.Transport.Usb;

public static class AndroidOpenAccessoryConstants
{
    public const ushort GoogleVendorId = 0x18D1;
    public const ushort AccessoryProductId = 0x2D00;
    public const ushort AccessoryAdbProductId = 0x2D01;
    public const ushort AudioProductId = 0x2D02;
    public const ushort AudioAdbProductId = 0x2D03;
    public const ushort AccessoryAudioProductId = 0x2D04;
    public const ushort AccessoryAudioAdbProductId = 0x2D05;

    public const byte VendorInRequestType = 0xC0;
    public const byte VendorOutRequestType = 0x40;
    public const byte GetProtocolRequest = 51;
    public const byte SendStringRequest = 52;
    public const byte StartAccessoryRequest = 53;

    public const ushort ManufacturerStringId = 0;
    public const ushort ModelStringId = 1;
    public const ushort DescriptionStringId = 2;
    public const ushort VersionStringId = 3;
    public const ushort UriStringId = 4;
    public const ushort SerialStringId = 5;

    public const int MaximumIdentityStringBytes = 256;

    public static bool HasAccessoryDataInterface(
        ushort vendorId,
        ushort productId)
    {
        if (vendorId != GoogleVendorId)
        {
            return false;
        }

        return productId is
            AccessoryProductId or
            AccessoryAdbProductId or
            AccessoryAudioProductId or
            AccessoryAudioAdbProductId;
    }
}
