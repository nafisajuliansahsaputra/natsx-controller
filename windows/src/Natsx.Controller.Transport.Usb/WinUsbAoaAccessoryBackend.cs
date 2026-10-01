using Windows.Devices.Enumeration;
using Windows.Devices.Usb;

namespace Natsx.Controller.Transport.Usb;

/// <summary>
/// Opens Android devices that have already re-enumerated into Android Open
/// Accessory mode and are bound to the Microsoft WinUSB function driver.
///
/// This backend intentionally does not perform pre-AOA bootstrap against the
/// phone's normal MTP/PTP configuration.
/// </summary>
public sealed class WinUsbAoaAccessoryBackend : IAoaAccessoryDataBackend
{
    public async ValueTask<IReadOnlyList<WinUsbAoaAccessoryDevice>>
        EnumerateAsync(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<WinUsbAoaAccessoryDevice>();

        foreach (ushort productId in
            AoaAccessoryWinUsbConstants.SupportedProductIds)
        {
            string selector =
                UsbDevice.GetDeviceSelector(
                    AndroidOpenAccessoryConstants.GoogleVendorId,
                    productId,
                    AoaAccessoryWinUsbConstants.DeviceInterfaceGuid);

            DeviceInformationCollection devices =
                await DeviceInformation.FindAllAsync(selector);

            cancellationToken.ThrowIfCancellationRequested();

            foreach (DeviceInformation device in devices)
            {
                results.Add(
                    new WinUsbAoaAccessoryDevice(
                        device.Id,
                        device.Name,
                        AndroidOpenAccessoryConstants.GoogleVendorId,
                        productId));
            }
        }

        return results;
    }

    public async ValueTask<WinUsbAoaAccessoryConnection?>
        OpenFirstAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WinUsbAoaAccessoryDevice> devices =
            await EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);

        foreach (WinUsbAoaAccessoryDevice candidate in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WinUsbAoaAccessoryConnection? connection =
                await TryOpenAsync(
                    candidate,
                    cancellationToken)
                    .ConfigureAwait(false);

            if (connection is not null)
            {
                return connection;
            }
        }

        return null;
    }

    public async ValueTask<WinUsbAoaAccessoryConnection?>
        TryOpenAsync(
            WinUsbAoaAccessoryDevice candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.VendorId !=
                AndroidOpenAccessoryConstants.GoogleVendorId ||
            !AoaAccessoryWinUsbConstants
                .IsSupportedProductId(candidate.ProductId))
        {
            throw new ArgumentException(
                "Device is not a supported Android Open Accessory WinUSB target.",
                nameof(candidate));
        }

        cancellationToken.ThrowIfCancellationRequested();

        UsbDevice? device =
            await UsbDevice.FromIdAsync(
                candidate.DeviceId);

        cancellationToken.ThrowIfCancellationRequested();

        if (device is null)
        {
            return null;
        }

        try
        {
            UsbInterface dataInterface =
                device.DefaultInterface;

            if (dataInterface.BulkInPipes.Count == 0 ||
                dataInterface.BulkOutPipes.Count == 0)
            {
                return null;
            }

            Stream input =
                dataInterface.BulkInPipes[0]
                    .InputStream
                    .AsStreamForRead(bufferSize: 0);

            Stream output =
                new WinRtUsbDirectOutputStream(
                    dataInterface.BulkOutPipes[0]
                        .OutputStream);

            return new WinUsbAoaAccessoryConnection(
                device,
                candidate,
                input,
                output);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}
