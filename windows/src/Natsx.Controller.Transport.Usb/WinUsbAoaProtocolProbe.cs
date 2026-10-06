using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Transport.Usb;

/// <summary>
/// Performs a read-only Android Open Accessory protocol-version probe through
/// the USB device interface that Windows already exposes for the selected
/// device instance.
///
/// This probe never sends AOA strings and never sends START_ACCESSORY. It is
/// intentionally separate from the production bootstrap provider so we can
/// prove whether the inbox MTP WinUSB lower filter is directly usable from
/// user mode before installing any NATSX kernel filter.
/// </summary>
public static class WinUsbAoaProtocolProbe
{
    private static readonly Guid UsbDeviceInterfaceGuid =
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const uint PresentInterfacesOnly = 0;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;

    private const byte VendorDeviceToHost = 0xC0;
    private const byte AoaGetProtocol = 51;

    public static WinUsbAoaProtocolProbeResult Probe(
        string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);

        if (!OperatingSystem.IsWindows())
        {
            return WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath: null,
                stage: "platform",
                errorCode: null,
                message: "The WinUSB AOA protocol probe only runs on Windows.");
        }

        IReadOnlyList<string> paths;

        try
        {
            paths = EnumerateUsbDeviceInterfacePaths(deviceInstanceId);
        }
        catch (IOException exception)
        {
            return WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath: null,
                stage: "enumerate-interface",
                errorCode: null,
                message: exception.Message);
        }

        if (paths.Count == 0)
        {
            return WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath: null,
                stage: "enumerate-interface",
                errorCode: null,
                message:
                    "Windows did not expose GUID_DEVINTERFACE_USB_DEVICE for the selected device instance.");
        }

        WinUsbAoaProtocolProbeResult? lastFailure = null;

        foreach (string path in paths)
        {
            WinUsbAoaProtocolProbeResult result =
                ProbePath(deviceInstanceId, path);

            if (result.Success)
            {
                return result;
            }

            lastFailure = result;
        }

        return lastFailure ??
            WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath: null,
                stage: "probe",
                errorCode: null,
                message: "No USB device interface path could be probed.");
    }

    private static WinUsbAoaProtocolProbeResult ProbePath(
        string deviceInstanceId,
        string devicePath)
    {
        using SafeFileHandle deviceHandle =
            NativeMethods.CreateFileW(
                devicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal | FileFlagOverlapped,
                IntPtr.Zero);

        if (deviceHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();

            return WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath,
                "open-device-interface",
                error,
                DescribeWin32Error(error));
        }

        if (!NativeMethods.WinUsb_Initialize(
                deviceHandle,
                out IntPtr winUsbHandle))
        {
            int error = Marshal.GetLastWin32Error();

            return WinUsbAoaProtocolProbeResult.Failed(
                deviceInstanceId,
                devicePath,
                "winusb-initialize",
                error,
                DescribeWin32Error(error));
        }

        try
        {
            var setupPacket =
                new WinUsbSetupPacket
                {
                    RequestType = VendorDeviceToHost,
                    Request = AoaGetProtocol,
                    Value = 0,
                    Index = 0,
                    Length = sizeof(ushort),
                };

            var response =
                new byte[sizeof(ushort)];

            if (!NativeMethods.WinUsb_ControlTransfer(
                    winUsbHandle,
                    setupPacket,
                    response,
                    (uint)response.Length,
                    out uint transferred,
                    IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();

                return WinUsbAoaProtocolProbeResult.Failed(
                    deviceInstanceId,
                    devicePath,
                    "aoa-get-protocol",
                    error,
                    DescribeWin32Error(error));
            }

            if (transferred != sizeof(ushort))
            {
                return WinUsbAoaProtocolProbeResult.Failed(
                    deviceInstanceId,
                    devicePath,
                    "aoa-get-protocol",
                    errorCode: null,
                    message:
                        $"AOA GET_PROTOCOL returned {transferred} bytes; expected {sizeof(ushort)}.");
            }

            ushort protocolVersion =
                (ushort)(
                    response[0] |
                    (response[1] << 8));

            if (protocolVersion == 0)
            {
                return WinUsbAoaProtocolProbeResult.Failed(
                    deviceInstanceId,
                    devicePath,
                    "aoa-get-protocol",
                    errorCode: null,
                    message:
                        "The USB request completed but the device returned AOA protocol version 0.");
            }

            return new WinUsbAoaProtocolProbeResult(
                DeviceInstanceId: deviceInstanceId,
                DevicePath: devicePath,
                Success: true,
                Stage: "complete",
                Win32Error: null,
                Message:
                    "WinUSB initialized and AOA GET_PROTOCOL completed without changing USB mode.",
                AoaProtocolVersion: protocolVersion);
        }
        finally
        {
            _ = NativeMethods.WinUsb_Free(winUsbHandle);
        }
    }

    private static IReadOnlyList<string> EnumerateUsbDeviceInterfacePaths(
        string deviceInstanceId)
    {
        Guid interfaceGuid = UsbDeviceInterfaceGuid;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result =
                NativeMethods.CM_Get_Device_Interface_List_SizeW(
                    out uint requiredCharacters,
                    ref interfaceGuid,
                    deviceInstanceId,
                    PresentInterfacesOnly);

            if (result != CrSuccess)
            {
                throw CreateConfigManagerException(
                    "Could not query the USB device interface list size.",
                    result);
            }

            if (requiredCharacters <= 1)
            {
                return Array.Empty<string>();
            }

            var buffer =
                new char[requiredCharacters];

            result =
                NativeMethods.CM_Get_Device_Interface_ListW(
                    ref interfaceGuid,
                    deviceInstanceId,
                    buffer,
                    (uint)buffer.Length,
                    PresentInterfacesOnly);

            if (result == CrBufferSmall)
            {
                continue;
            }

            if (result != CrSuccess)
            {
                throw CreateConfigManagerException(
                    "Could not enumerate USB device interfaces.",
                    result);
            }

            return ParseMultiString(buffer);
        }

        throw new IOException(
            "The USB device interface list changed repeatedly during enumeration.");
    }

    private static IReadOnlyList<string> ParseMultiString(
        char[] buffer)
    {
        var results =
            new List<string>();

        int start = 0;

        for (int index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\0')
            {
                continue;
            }

            if (index == start)
            {
                break;
            }

            results.Add(
                new string(
                    buffer,
                    start,
                    index - start));

            start = index + 1;
        }

        return results;
    }

    private static IOException CreateConfigManagerException(
        string message,
        int configManagerResult) =>
        new(
            $"{message} Configuration Manager result: 0x{configManagerResult:X8}.");

    private static string DescribeWin32Error(
        int error) =>
        $"Win32 error {error}: {new Win32Exception(error).Message}";

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 1)]
    private struct WinUsbSetupPacket
    {
        public byte RequestType;
        public byte Request;
        public ushort Value;
        public ushort Index;
        public ushort Length;
    }

    private static class NativeMethods
    {
        [DllImport(
            "cfgmgr32.dll",
            CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_Interface_List_SizeW(
            out uint pulLen,
            ref Guid interfaceClassGuid,
            string? deviceId,
            uint flags);

        [DllImport(
            "cfgmgr32.dll",
            CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_Interface_ListW(
            ref Guid interfaceClassGuid,
            string? deviceId,
            [Out] char[] buffer,
            uint bufferLength,
            uint flags);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_Initialize(
            SafeFileHandle deviceHandle,
            out IntPtr interfaceHandle);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_ControlTransfer(
            IntPtr interfaceHandle,
            WinUsbSetupPacket setupPacket,
            [Out] byte[] buffer,
            uint bufferLength,
            out uint lengthTransferred,
            IntPtr overlapped);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_Free(
            IntPtr interfaceHandle);
    }
}

public sealed record WinUsbAoaProtocolProbeResult(
    string DeviceInstanceId,
    string? DevicePath,
    bool Success,
    string Stage,
    int? Win32Error,
    string Message,
    ushort? AoaProtocolVersion)
{
    internal static WinUsbAoaProtocolProbeResult Failed(
        string deviceInstanceId,
        string? devicePath,
        string stage,
        int? errorCode,
        string message) =>
        new(
            DeviceInstanceId: deviceInstanceId,
            DevicePath: devicePath,
            Success: false,
            Stage: stage,
            Win32Error: errorCode,
            Message: message,
            AoaProtocolVersion: null);
}
