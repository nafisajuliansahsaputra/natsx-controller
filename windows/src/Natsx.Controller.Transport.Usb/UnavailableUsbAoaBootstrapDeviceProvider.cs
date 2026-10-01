namespace Natsx.Controller.Transport.Usb;

public sealed class UnavailableUsbAoaBootstrapDeviceProvider :
    IUsbAoaBootstrapDeviceProvider
{
    public UnavailableUsbAoaBootstrapDeviceProvider(
        UsbBootstrapProviderState state =
            UsbBootstrapProviderState.Unavailable)
    {
        if (state == UsbBootstrapProviderState.Ready)
        {
            throw new ArgumentException(
                "Unavailable provider cannot report Ready.",
                nameof(state));
        }

        State = state;
    }

    public UsbBootstrapProviderState State { get; }

    public ValueTask<IReadOnlyList<IUsbAoaBootstrapDevice>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IUsbAoaBootstrapDevice> empty =
            Array.Empty<IUsbAoaBootstrapDevice>();

        return ValueTask.FromResult(empty);
    }
}
