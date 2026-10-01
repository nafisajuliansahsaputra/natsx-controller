namespace Natsx.Controller.Transport.Usb;

public static class AndroidOpenAccessory
{
    public const ushort GoogleVendorId = 0x18D1;
    public const ushort AccessoryProductId = 0x2D00;

    public const byte GetProtocolRequest = 51;
    public const byte SendStringRequest = 52;
    public const byte StartAccessoryRequest = 53;

    public const string Manufacturer = "NATSX";
    public const string Model = "NATSX Controller";
    public const string Description = "NATSX direct Android controller transport";
    public const string Version = "1";

    public static bool IsAccessoryIdentity(
        ushort vendorId,
        ushort productId) =>
        vendorId == GoogleVendorId &&
        productId == AccessoryProductId;
}
