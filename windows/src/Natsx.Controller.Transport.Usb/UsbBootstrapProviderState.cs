namespace Natsx.Controller.Transport.Usb;

public enum UsbBootstrapProviderState
{
    Ready = 0,
    Unavailable = 1,
    DriverMissing = 2,
    RequiresElevation = 3,
}
