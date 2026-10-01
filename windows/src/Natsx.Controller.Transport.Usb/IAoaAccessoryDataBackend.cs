namespace Natsx.Controller.Transport.Usb;

public interface IAoaAccessoryDataBackend
{
    ValueTask<IReadOnlyList<WinUsbAoaAccessoryDevice>> EnumerateAsync(
        CancellationToken cancellationToken = default);

    ValueTask<WinUsbAoaAccessoryConnection?> OpenFirstAsync(
        CancellationToken cancellationToken = default);
}
