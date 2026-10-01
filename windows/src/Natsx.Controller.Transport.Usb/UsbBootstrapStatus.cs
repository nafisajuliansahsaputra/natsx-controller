namespace Natsx.Controller.Transport.Usb;

public enum UsbBootstrapStatus
{
    AccessoryAlreadyReady = 0,
    AccessoryStarted = 1,
    BootstrapBackendUnavailable = 2,
    BootstrapDriverMissing = 3,
    BootstrapRequiresElevation = 4,
    NoDataDeviceDetected = 5,
    AoaUnsupported = 6,
    AccessoryReenumerationTimeout = 7,
    BootstrapFailed = 8,
}

public sealed record UsbBootstrapResult(
    UsbBootstrapStatus Status,
    string? DeviceId = null,
    ushort? AoaProtocolVersion = null,
    string? Diagnostic = null)
{
    public bool IsReady =>
        Status is
            UsbBootstrapStatus.AccessoryAlreadyReady or
            UsbBootstrapStatus.AccessoryStarted;
}
