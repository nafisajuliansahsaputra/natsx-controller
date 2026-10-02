using Natsx.Controller.Transport.Usb;
using System.Text.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This diagnostic only runs on Windows.");
    return 10;
}

string? instanceId = null;

for (int index = 0; index < args.Length; index++)
{
    if (args[index] == "--instance-id" &&
        index + 1 < args.Length)
    {
        instanceId = args[++index];
        continue;
    }

    if (args[index] is "-h" or "--help")
    {
        PrintUsage();
        return 0;
    }

    Console.Error.WriteLine($"Unknown or incomplete argument: {args[index]}");
    PrintUsage();
    return 11;
}

if (string.IsNullOrWhiteSpace(instanceId))
{
    Console.Error.WriteLine("--instance-id is required.");
    PrintUsage();
    return 11;
}

WinUsbAoaProtocolProbeResult result =
    WinUsbAoaProtocolProbe.Probe(instanceId);

Console.WriteLine(
    JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

return result.Success ? 0 : 12;

static void PrintUsage()
{
    Console.WriteLine(
        "Usage: dotnet run --project windows/tools/Natsx.Controller.UsbProbe -- --instance-id \"USB\\VID_xxxx&PID_xxxx\\serial\"");
    Console.WriteLine();
    Console.WriteLine(
        "This probe opens the existing Windows USB device interface, attempts WinUsb_Initialize,");
    Console.WriteLine(
        "and sends only Android Open Accessory GET_PROTOCOL (request 51). It never sends");
    Console.WriteLine(
        "AOA identity strings or START_ACCESSORY, so it does not switch the phone into accessory mode.");
}
