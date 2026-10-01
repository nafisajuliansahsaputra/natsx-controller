namespace Natsx.Controller.Transport.Usb;

public sealed record WinUsbAoaAccessoryDevice(
    string DeviceId,
    string DisplayName,
    ushort VendorId,
    ushort ProductId);
