namespace Natsx.Controller.Transport.Usb;

public interface IUsbAoaBootstrapDevice : IAsyncDisposable
{
    string DeviceId { get; }

    ushort VendorId { get; }

    ushort ProductId { get; }

    ValueTask<ushort> StartAccessoryModeAsync(
        CancellationToken cancellationToken = default);
}

public interface IUsbAoaBootstrapDeviceProvider
{
    UsbBootstrapProviderState State { get; }

    ValueTask<IReadOnlyList<IUsbAoaBootstrapDevice>> EnumerateAsync(
        CancellationToken cancellationToken = default);
}
