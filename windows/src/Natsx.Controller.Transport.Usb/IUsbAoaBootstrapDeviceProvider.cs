namespace Natsx.Controller.Transport.Usb;

public interface IUsbAoaBootstrapDevice : IUsbControlTransferDevice, IAsyncDisposable
{
    string DeviceId { get; }

    ushort VendorId { get; }

    ushort ProductId { get; }
}

public interface IUsbAoaBootstrapDeviceProvider
{
    UsbBootstrapProviderState State { get; }

    ValueTask<IReadOnlyList<IUsbAoaBootstrapDevice>> EnumerateAsync(
        CancellationToken cancellationToken = default);
}
