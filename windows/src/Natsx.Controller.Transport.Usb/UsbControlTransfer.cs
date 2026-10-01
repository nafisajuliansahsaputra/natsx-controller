namespace Natsx.Controller.Transport.Usb;

public enum UsbControlDirection
{
    HostToDevice,
    DeviceToHost,
}

public readonly record struct UsbControlTransfer(
    UsbControlDirection Direction,
    byte Request,
    ushort Value,
    ushort Index,
    byte[] Data,
    int ExpectedLength = 0);

public interface IUsbBootstrapDevice
{
    ValueTask<byte[]> ControlTransferAsync(
        UsbControlTransfer transfer,
        CancellationToken cancellationToken);
}
