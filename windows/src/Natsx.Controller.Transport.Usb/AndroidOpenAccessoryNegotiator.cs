using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Transport.Usb;

public sealed class AndroidOpenAccessoryNegotiator
{
    private readonly AndroidOpenAccessoryMetadata _metadata;

    public AndroidOpenAccessoryNegotiator(
        AndroidOpenAccessoryMetadata? metadata = null)
    {
        _metadata =
            metadata ??
            AndroidOpenAccessoryMetadata.NatsxDefault;
    }

    public async ValueTask<ushort> StartAccessoryModeAsync(
        IUsbControlTransferDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var protocolBuffer = new byte[2];

        int bytesRead =
            await device.ControlInAsync(
                AndroidOpenAccessoryConstants.VendorInRequestType,
                AndroidOpenAccessoryConstants.GetProtocolRequest,
                value: 0,
                index: 0,
                protocolBuffer,
                cancellationToken).ConfigureAwait(false);

        if (bytesRead != protocolBuffer.Length)
        {
            throw new IOException(
                "AOA GET_PROTOCOL did not return the required 16-bit version.");
        }

        ushort protocolVersion =
            BinaryPrimitives.ReadUInt16LittleEndian(protocolBuffer);

        if (protocolVersion == 0)
        {
            throw new NotSupportedException(
                "Connected Android device does not advertise Android Open Accessory support.");
        }

        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.ManufacturerStringId,
            _metadata.Manufacturer,
            cancellationToken).ConfigureAwait(false);
        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.ModelStringId,
            _metadata.Model,
            cancellationToken).ConfigureAwait(false);
        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.DescriptionStringId,
            _metadata.Description,
            cancellationToken).ConfigureAwait(false);
        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.VersionStringId,
            _metadata.Version,
            cancellationToken).ConfigureAwait(false);
        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.UriStringId,
            _metadata.Uri,
            cancellationToken).ConfigureAwait(false);
        await SendIdentityStringAsync(
            device,
            AndroidOpenAccessoryConstants.SerialStringId,
            _metadata.Serial,
            cancellationToken).ConfigureAwait(false);

        await device.ControlOutAsync(
            AndroidOpenAccessoryConstants.VendorOutRequestType,
            AndroidOpenAccessoryConstants.StartAccessoryRequest,
            value: 0,
            index: 0,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);

        return protocolVersion;
    }

    private static async ValueTask SendIdentityStringAsync(
        IUsbControlTransferDevice device,
        ushort stringId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        byte[] text =
            Encoding.UTF8.GetBytes(value);

        if (text.Length + 1 >
            AndroidOpenAccessoryConstants.MaximumIdentityStringBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "AOA identity strings must fit within 256 bytes including the terminating zero.");
        }

        var terminated =
            new byte[text.Length + 1];

        text.CopyTo(terminated, 0);

        await device.ControlOutAsync(
            AndroidOpenAccessoryConstants.VendorOutRequestType,
            AndroidOpenAccessoryConstants.SendStringRequest,
            value: 0,
            index: stringId,
            terminated,
            cancellationToken).ConfigureAwait(false);
    }
}
