using Natsx.Controller.Transport.Usb;
using System.Text.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This diagnostic only runs on Windows.");
    return 10;
}

var backend = new WinUsbAoaAccessoryBackend();

IReadOnlyList<WinUsbAoaAccessoryDevice> devices =
    await backend.EnumerateAsync();

var report = new
{
    DeviceCount = devices.Count,
    Devices = devices,
    Opened = false,
    BulkInReadable = false,
    BulkOutWritable = false,
    OpenedDevice = (WinUsbAoaAccessoryDevice?)null,
};

if (devices.Count == 0)
{
    Console.WriteLine(
        JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

    return 12;
}

await using WinUsbAoaAccessoryConnection? connection =
    await backend.OpenFirstAsync();

if (connection is null)
{
    Console.WriteLine(
        JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

    return 13;
}

var successReport = new
{
    DeviceCount = devices.Count,
    Devices = devices,
    Opened = true,
    BulkInReadable = connection.Input.CanRead,
    BulkOutWritable = connection.Output.CanWrite,
    OpenedDevice = connection.Identity,
};

Console.WriteLine(
    JsonSerializer.Serialize(
        successReport,
        new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

return connection.Input.CanRead &&
       connection.Output.CanWrite
    ? 0
    : 14;
