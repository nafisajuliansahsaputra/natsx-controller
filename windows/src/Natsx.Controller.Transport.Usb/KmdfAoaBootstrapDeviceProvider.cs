using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Transport.Usb;

public sealed class KmdfAoaBootstrapDeviceProvider :
    IUsbAoaBootstrapDeviceProvider
{
    public UsbBootstrapProviderState State =>
        OperatingSystem.IsWindows()
            ? UsbBootstrapProviderState.Ready
            : UsbBootstrapProviderState.Unavailable;

    public ValueTask<IReadOnlyList<IUsbAoaBootstrapDevice>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            IReadOnlyList<IUsbAoaBootstrapDevice> empty =
                Array.Empty<IUsbAoaBootstrapDevice>();

            return ValueTask.FromResult(empty);
        }

        IReadOnlyList<string> paths =
            AoaBootstrapDeviceInterfaceEnumerator.EnumeratePresentPaths();

        var devices =
            new List<IUsbAoaBootstrapDevice>(paths.Count);

        try
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    devices.Add(
                        KmdfAoaBootstrapDevice.Open(path));
                }
                catch (IOException)
                {
                    // A device can disappear while SetupAPI/ConfigMgr is
                    // enumerating. Treat that path as stale and continue.
                }
            }

            return ValueTask.FromResult<IReadOnlyList<IUsbAoaBootstrapDevice>>(
                devices);
        }
        catch
        {
            foreach (IUsbAoaBootstrapDevice device in devices)
            {
                device.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            throw;
        }
    }
}

internal sealed class KmdfAoaBootstrapDevice :
    IUsbAoaBootstrapDevice
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorAccessDenied = 5;

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private KmdfAoaBootstrapDevice(
        string devicePath,
        SafeFileHandle handle,
        AoaBootstrapDriverVersion driverVersion)
    {
        DeviceId = devicePath;
        _handle = handle;
        DriverVersion = driverVersion;
    }

    public string DeviceId { get; }

    public ushort VendorId => 0;

    public ushort ProductId => 0;

    public AoaBootstrapDriverVersion DriverVersion { get; }

    public static KmdfAoaBootstrapDevice Open(
        string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        SafeFileHandle handle =
            NativeMethods.CreateFileW(
                devicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            if (error == ErrorAccessDenied)
            {
                throw new UnauthorizedAccessException(
                    "Access to the NATSX AOA bootstrap driver interface was denied.");
            }

            throw CreateIoException(
                "Could not open the NATSX AOA bootstrap driver interface.",
                error);
        }

        try
        {
            byte[] response =
                DeviceIoControl(
                    handle,
                    AoaBootstrapDriverProtocol.GetVersionControlCode,
                    AoaBootstrapDriverProtocol.VersionResponseSize);

            AoaBootstrapDriverVersion version =
                AoaBootstrapDriverProtocol.ParseVersion(response);

            if (version.ProtocolVersion != 1)
            {
                throw new InvalidOperationException(
                    $"Unsupported NATSX AOA bootstrap driver protocol version {version.ProtocolVersion}.");
            }

            return new KmdfAoaBootstrapDevice(
                devicePath,
                handle,
                version);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public ValueTask<ushort> StartAccessoryModeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        byte[] response =
            DeviceIoControl(
                _handle,
                AoaBootstrapDriverProtocol.StartAoaControlCode,
                AoaBootstrapDriverProtocol.StartResponseSize);

        ushort version =
            AoaBootstrapDriverProtocol.ParseAoaProtocolVersion(response);

        if (version == 0)
        {
            throw new NotSupportedException(
                "Connected Android device does not advertise Android Open Accessory support.");
        }

        return ValueTask.FromResult(version);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _handle.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static byte[] DeviceIoControl(
        SafeFileHandle handle,
        uint controlCode,
        int expectedResponseSize)
    {
        var response =
            new byte[expectedResponseSize];

        if (!NativeMethods.DeviceIoControl(
                handle,
                controlCode,
                IntPtr.Zero,
                0,
                response,
                (uint)response.Length,
                out uint bytesReturned,
                IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();

            if (error == ErrorAccessDenied)
            {
                throw new UnauthorizedAccessException(
                    "The NATSX AOA bootstrap driver denied the request.");
            }

            throw CreateIoException(
                "The NATSX AOA bootstrap driver request failed.",
                error);
        }

        if (bytesReturned < expectedResponseSize)
        {
            throw new IOException(
                $"The NATSX AOA bootstrap driver returned {bytesReturned} bytes; expected at least {expectedResponseSize}.");
        }

        return response;
    }

    private static IOException CreateIoException(
        string message,
        int error) =>
        new(
            $"{message} Win32 error {error}: " +
            new Win32Exception(error).Message);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static class NativeMethods
    {
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
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            [Out] byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}

internal static class AoaBootstrapDeviceInterfaceEnumerator
{
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const uint PresentInterfacesOnly = 0;

    public static IReadOnlyList<string> EnumeratePresentPaths()
    {
        Guid interfaceGuid =
            AoaBootstrapDriverProtocol.DeviceInterfaceGuid;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result =
                NativeMethods.CM_Get_Device_Interface_List_SizeW(
                    out uint requiredCharacters,
                    ref interfaceGuid,
                    null,
                    PresentInterfacesOnly);

            if (result != CrSuccess)
            {
                throw CreateConfigManagerException(
                    "Could not query NATSX AOA bootstrap interface list size.",
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
                    null,
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
                    "Could not enumerate NATSX AOA bootstrap interfaces.",
                    result);
            }

            return ParseMultiString(buffer);
        }

        throw new IOException(
            "NATSX AOA bootstrap interface list changed repeatedly during enumeration.");
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
    }
}
