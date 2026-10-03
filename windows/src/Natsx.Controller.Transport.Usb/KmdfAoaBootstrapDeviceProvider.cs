using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Transport.Usb;

public sealed class KmdfAoaBootstrapDeviceProvider :
    IUsbAoaBootstrapDeviceProvider
{
    public UsbBootstrapProviderState State
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return UsbBootstrapProviderState.Unavailable;
            }

            try
            {
                using RegistryKey? serviceKey =
                    Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Services\{AoaBootstrapDriverProtocol.ServiceName}",
                        writable: false);

                return serviceKey is null
                    ? UsbBootstrapProviderState.DriverMissing
                    : UsbBootstrapProviderState.Ready;
            }
            catch (UnauthorizedAccessException)
            {
                return UsbBootstrapProviderState.RequiresElevation;
            }
            catch (System.Security.SecurityException)
            {
                return UsbBootstrapProviderState.RequiresElevation;
            }
        }
    }

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

        IUsbAoaBootstrapDevice device = KmdfAoaBootstrapDevice.OpenSidebandControl();
        IReadOnlyList<IUsbAoaBootstrapDevice> devices = new[] { device };
        return ValueTask.FromResult(devices);

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

    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private KmdfAoaBootstrapDevice(
        SafeFileHandle handle,
        AoaBootstrapDriverVersion driverVersion)
    {
        DeviceId =
            AoaBootstrapDriverProtocol.ControlDevicePath;
        _handle = handle;
        DriverVersion = driverVersion;
    }

    public string DeviceId { get; }

    public ushort VendorId => 0;

    public ushort ProductId => 0;

    public AoaBootstrapDriverVersion DriverVersion { get; }

    public static KmdfAoaBootstrapDevice OpenSidebandControl()
    {
        SafeFileHandle handle =
            NativeMethods.CreateFileW(
                AoaBootstrapDriverProtocol.ControlDevicePath,
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

            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                throw new BootstrapControlDeviceMissingException();
            }

            if (error == ErrorAccessDenied)
            {
                throw new UnauthorizedAccessException(
                    "Access to the NATSX AOA bootstrap sideband control device was denied.");
            }

            throw CreateIoException(
                "Could not open the NATSX AOA bootstrap sideband control device.",
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

            AoaBootstrapDriverProtocol
                .ValidateCompatibility(version);

            byte[] statusResponse =
                DeviceIoControl(
                    handle,
                    AoaBootstrapDriverProtocol.GetStatusControlCode,
                    AoaBootstrapDriverProtocol.StatusResponseSize);

            AoaBootstrapDriverStatus status =
                AoaBootstrapDriverProtocol.ParseStatus(statusResponse);

            if (status.ProtocolVersion != version.ProtocolVersion ||
                status.DriverBuild != version.DriverBuild)
            {
                throw new IOException(
                    "The NATSX AOA bootstrap GET_STATUS response does not match GET_VERSION.");
            }

            if (status.AttachedTargetCount != 1)
            {
                throw new BootstrapTargetNotReadyException(
                    $"Expected exactly one attached bootstrap target, found {status.AttachedTargetCount}.");
            }

            byte[] rawProbeResponse =
                DeviceIoControl(
                    handle,
                    AoaBootstrapDriverProtocol.ProbeProtocolRawControlCode,
                    AoaBootstrapDriverProtocol.RawProtocolProbeResponseSize);

            AoaBootstrapRawProtocolProbe rawProbe =
                AoaBootstrapDriverProtocol.ParseRawProtocolProbe(
                    rawProbeResponse);

            if (rawProbe.ProtocolVersion != version.ProtocolVersion ||
                rawProbe.DriverBuild != version.DriverBuild)
            {
                throw new IOException(
                    "The NATSX raw AOA protocol probe does not match GET_VERSION.");
            }

            if (rawProbe.SubmitStatus != 0 ||
                rawProbe.UsbStatus != 0 ||
                rawProbe.BytesTransferred != 2 ||
                rawProbe.AoaProtocolVersion == 0 ||
                rawProbe.Reserved != 0)
            {
                throw new BootstrapTargetNotReadyException(
                    $"Raw endpoint-zero probe is not ready. " +
                    $"SubmitStatus=0x{unchecked((uint)rawProbe.SubmitStatus):X8}, " +
                    $"UsbStatus=0x{rawProbe.UsbStatus:X8}, " +
                    $"Bytes={rawProbe.BytesTransferred}, " +
                    $"AoaProtocol={rawProbe.AoaProtocolVersion}.");
            }

            return new KmdfAoaBootstrapDevice(
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
                    "The NATSX AOA bootstrap sideband control device denied the request.");
            }

            if (error == ErrorNotSupported)
            {
                throw new NotSupportedException(
                    "The connected Android device does not support the requested AOA bootstrap operation.");
            }

            throw CreateIoException(
                "The NATSX AOA bootstrap sideband request failed.",
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

internal sealed class BootstrapTargetNotReadyException :
    IOException
{
    public BootstrapTargetNotReadyException(
        string diagnostic)
        : base(
            "The NATSX AOA bootstrap target is not ready. " +
            diagnostic)
    {
    }

    public BootstrapTargetNotReadyException(
        uint attachedTargetCount,
        uint readyUsbTargetCount,
        int lastUsbTargetCreateStatus,
        uint usbTargetCreateAttemptCount)
        : this(
            $"Attached={attachedTargetCount}, Ready={readyUsbTargetCount}, " +
            $"LastCreateStatus=0x{unchecked((uint)lastUsbTargetCreateStatus):X8}, " +
            $"CreateAttempts={usbTargetCreateAttemptCount}.")
    {
    }
}

internal sealed class BootstrapControlDeviceMissingException :
    IOException
{
    public BootstrapControlDeviceMissingException()
        : base(
            "The NATSX AOA bootstrap control device is not loaded. Check that the bootstrap driver is installed and allowed to load by Windows; reconnect the Android USB data cable.")
    {
    }
}
