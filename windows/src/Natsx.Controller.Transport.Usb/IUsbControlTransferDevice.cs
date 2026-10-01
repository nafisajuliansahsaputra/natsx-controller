namespace Natsx.Controller.Transport.Usb;

public interface IUsbControlTransferDevice
{
    ValueTask<int> ControlInAsync(
        byte requestType,
        byte request,
        ushort value,
        ushort index,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default);

    ValueTask ControlOutAsync(
        byte requestType,
        byte request,
        ushort value,
        ushort index,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
