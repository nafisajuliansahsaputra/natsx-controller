using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Transport.Usb;

/// <summary>
/// Native WinUSB bulk-OUT writer.
///
/// This deliberately bypasses the WinRT UsbDevice/IOutputStream layer. The
/// physical OPPO A58 validation path proved Android-to-Windows bulk IN works
/// while every WinRT bulk OUT adapter completed locally without delivering
/// bytes to Android. WinUsb_WritePipe gives us the native transfer result and
/// transferred-byte count from the actual endpoint.
/// </summary>
internal sealed class NativeWinUsbBulkOutStream : Stream
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly SafeFileHandle _deviceHandle;
    private readonly SemaphoreSlim _writeGate =
        new(1, 1);

    private IntPtr _winUsbHandle;
    private bool _disposed;

    private NativeWinUsbBulkOutStream(
        SafeFileHandle deviceHandle,
        IntPtr winUsbHandle,
        byte pipeId,
        ushort maximumPacketSize)
    {
        _deviceHandle = deviceHandle;
        _winUsbHandle = winUsbHandle;
        PipeId = pipeId;
        MaximumPacketSize = maximumPacketSize;
    }

    public byte PipeId { get; }

    public ushort MaximumPacketSize { get; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static NativeWinUsbBulkOutStream Open(
        string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            devicePath);

        SafeFileHandle deviceHandle =
            NativeMethods.CreateFileW(
                devicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                // WinUsb_Initialize requires an overlapped device handle,
                // even when individual ReadPipe/WritePipe calls wait synchronously.
                FileAttributeNormal | FileFlagOverlapped,
                IntPtr.Zero);

        if (deviceHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            deviceHandle.Dispose();

            throw new IOException(
                $"Could not open AOA WinUSB interface for native bulk OUT. {DescribeWin32Error(error)}");
        }

        if (!NativeMethods.WinUsb_Initialize(
                deviceHandle,
                out IntPtr winUsbHandle))
        {
            int error = Marshal.GetLastWin32Error();
            deviceHandle.Dispose();

            throw new IOException(
                $"WinUsb_Initialize failed for AOA native bulk OUT. {DescribeWin32Error(error)}");
        }

        try
        {
            if (!NativeMethods.WinUsb_QueryInterfaceSettings(
                    winUsbHandle,
                    0,
                    out UsbInterfaceDescriptor descriptor))
            {
                int error = Marshal.GetLastWin32Error();

                throw new IOException(
                    $"WinUsb_QueryInterfaceSettings failed. {DescribeWin32Error(error)}");
            }

            for (byte index = 0;
                 index < descriptor.NumEndpoints;
                 index++)
            {
                if (!NativeMethods.WinUsb_QueryPipe(
                        winUsbHandle,
                        0,
                        index,
                        out WinUsbPipeInformation pipe))
                {
                    int error = Marshal.GetLastWin32Error();

                    throw new IOException(
                        $"WinUsb_QueryPipe failed for endpoint index {index}. {DescribeWin32Error(error)}");
                }

                bool isBulk =
                    pipe.PipeType ==
                    UsbdPipeType.Bulk;

                bool isOut =
                    (pipe.PipeId & 0x80) == 0;

                if (isBulk && isOut)
                {
                    return new NativeWinUsbBulkOutStream(
                        deviceHandle,
                        winUsbHandle,
                        pipe.PipeId,
                        pipe.MaximumPacketSize);
                }
            }

            throw new IOException(
                "AOA WinUSB interface does not expose a native bulk OUT endpoint.");
        }
        catch
        {
            _ = NativeMethods.WinUsb_Free(
                winUsbHandle);

            deviceHandle.Dispose();
            throw;
        }
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        // WinUsb_WritePipe is the transfer boundary.
    }

    public override Task FlushAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(
        long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(
            buffer);

        ValidateRange(
            buffer.Length,
            offset,
            count);

        WriteCoreAsync(
                buffer.AsMemory(
                    offset,
                    count),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            buffer);

        ValidateRange(
            buffer.Length,
            offset,
            count);

        return WriteCoreAsync(
                buffer.AsMemory(
                    offset,
                    count),
                cancellationToken)
            .AsTask();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(
            buffer,
            cancellationToken);

    protected override void Dispose(
        bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;

        if (_winUsbHandle != IntPtr.Zero)
        {
            _ = NativeMethods.WinUsb_AbortPipe(
                _winUsbHandle,
                PipeId);

            _ = NativeMethods.WinUsb_Free(
                _winUsbHandle);

            _winUsbHandle =
                IntPtr.Zero;
        }

        _deviceHandle.Dispose();

        if (disposing)
        {
            _writeGate.Dispose();
        }

        base.Dispose(disposing);
    }

    private async ValueTask WriteCoreAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.IsEmpty)
        {
            return;
        }

        await _writeGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            cancellationToken.ThrowIfCancellationRequested();

            byte[] bytes =
                buffer.ToArray();

            if (!NativeMethods.WinUsb_WritePipe(
                    _winUsbHandle,
                    PipeId,
                    bytes,
                    (uint)bytes.Length,
                    out uint transferred,
                    IntPtr.Zero))
            {
                int error =
                    Marshal.GetLastWin32Error();

                throw new IOException(
                    $"WinUsb_WritePipe failed on pipe 0x{PipeId:X2}. {DescribeWin32Error(error)}");
            }

            if (transferred !=
                (uint)bytes.Length)
            {
                throw new IOException(
                    $"WinUsb_WritePipe transferred {transferred} of {bytes.Length} bytes on pipe 0x{PipeId:X2}.");
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void ValidateRange(
        int length,
        int offset,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            offset);
        ArgumentOutOfRangeException.ThrowIfNegative(
            count);

        if (offset > length ||
            count > length - offset)
        {
            throw new ArgumentException(
                "Offset and count exceed the buffer length.");
        }
    }

    private static string DescribeWin32Error(
        int error) =>
        $"Win32 error {error}: {new Win32Exception(error).Message}";

    private enum UsbdPipeType
    {
        Control = 0,
        Isochronous = 1,
        Bulk = 2,
        Interrupt = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte Interface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public UsbdPipeType PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
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
        internal static extern bool WinUsb_QueryInterfaceSettings(
            IntPtr interfaceHandle,
            byte alternateInterfaceNumber,
            out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_QueryPipe(
            IntPtr interfaceHandle,
            byte alternateInterfaceNumber,
            byte pipeIndex,
            out WinUsbPipeInformation pipeInformation);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_WritePipe(
            IntPtr interfaceHandle,
            byte pipeId,
            [In] byte[] buffer,
            uint bufferLength,
            out uint lengthTransferred,
            IntPtr overlapped);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_AbortPipe(
            IntPtr interfaceHandle,
            byte pipeId);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_Free(
            IntPtr interfaceHandle);
    }
}
