using System.Buffers.Binary;
using System.Text;

namespace Natsx.Controller.Transport.Usb;

public sealed class AndroidOpenAccessoryNegotiator
{
    private static readonly string[] IdentificationStrings =
    [
        AndroidOpenAccessory.Manufacturer,
        AndroidOpenAccessory.Model,
        AndroidOpenAccessory.Description,
        AndroidOpenAccessory.Version,
        "https://natsx.local/controller",
        "NATSX",
    ];

    public async ValueTask<ushort> EnterAccessoryModeAsync(
        IUsbBootstrapDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] protocolBytes =
            await device.ControlTransferAsync(
                new UsbControlTransfer(
                    UsbControlDirection.DeviceToHost,
                    AndroidOpenAccessory.GetProtocolRequest,
                    Value: 0,
                    Index: 0,
                    Data: [],
                    ExpectedLength: 2),
                cancellationToken)
                .ConfigureAwait(false);

        if (protocolBytes.Length != 2)
        {
            throw new InvalidDataException(
                "AOA GET_PROTOCOL must return exactly two bytes.");
        }

        ushort protocolVersion =
            BinaryPrimitives.ReadUInt16LittleEndian(
                protocolBytes);

        if (protocolVersion < 1)
        {
            throw new NotSupportedException(
                "Android device does not support AOA v1 generic accessory mode.");
        }

        for (ushort index = 0;
             index < IdentificationStrings.Length;
             index++)
        {
            byte[] value =
                Encoding.UTF8.GetBytes(
                    IdentificationStrings[index] + "\0");

            await device.ControlTransferAsync(
                new UsbControlTransfer(
                    UsbControlDirection.HostToDevice,
                    AndroidOpenAccessory.SendStringRequest,
                    Value: 0,
                    Index: index,
                    Data: value),
                cancellationToken)
                .ConfigureAwait(false);
        }

        await device.ControlTransferAsync(
            new UsbControlTransfer(
                UsbControlDirection.HostToDevice,
                AndroidOpenAccessory.StartAccessoryRequest,
                Value: 0,
                Index: 0,
                Data: []),
            cancellationToken)
            .ConfigureAwait(false);

        return protocolVersion;
    }
}
