using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Transport.Usb;

/// <summary>
/// Owns one native WinUSB handle for the AOA data interface and exposes the
/// bulk IN/OUT endpoints as Streams.
///
/// Keeping both directions on one WinUSB interface avoids mixing the WinRT
/// UsbDevice data plane with a second native handle.
/// </summary>
internal sealed class NativeWinUsbAoaConnectionOwner : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint PipeTransferTimeoutPolicy = 3;
    private const int ErrorSemTimeout = 121;

    private readonly SafeFileHandle _deviceHandle;
    private readonly SemaphoreSlim _readGate =
        new(1, 1);
    private readonly SemaphoreSlim _writeGate =
        new(1, 1);

    private IntPtr _winUsbHandle;
    private bool _disposed;

    private NativeWinUsbAoaConnectionOwner(
        SafeFileHandle deviceHandle,
        IntPtr winUsbHandle,
        byte bulkInPipeId,
        byte bulkOutPipeId,
        ushort maximumPacketSize)
    {
        _deviceHandle = deviceHandle;
        _winUsbHandle = winUsbHandle;
        BulkInPipeId = bulkInPipeId;
        BulkOutPipeId = bulkOutPipeId;
        MaximumPacketSize = maximumPacketSize;

        Input =
            new NativeWinUsbPipeStream(
                this,
                canRead: true,
                canWrite: false);

        Output =
            new NativeWinUsbPipeStream(
                this,
                canRead: false,
                canWrite: true);
    }

    public byte BulkInPipeId { get; }

    public byte BulkOutPipeId { get; }

    public ushort MaximumPacketSize { get; }

    public Stream Input { get; }

    public Stream Output { get; }

    public static NativeWinUsbAoaConnectionOwner Open(
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
                FileAttributeNormal,
                IntPtr.Zero);

        if (deviceHandle.IsInvalid)
        {
            int error =
                Marshal.GetLastWin32Error();

            deviceHandle.Dispose();

            throw new IOException(
                $"Could not open AOA WinUSB interface. {DescribeWin32Error(error)}");
        }

        if (!NativeMethods.WinUsb_Initialize(
                deviceHandle,
                out IntPtr winUsbHandle))
        {
            int error =
                Marshal.GetLastWin32Error();

            deviceHandle.Dispose();

            throw new IOException(
                $"WinUsb_Initialize failed for AOA data interface. {DescribeWin32Error(error)}");
        }

        try
        {
            if (!NativeMethods.WinUsb_QueryInterfaceSettings(
                    winUsbHandle,
                    0,
                    out UsbInterfaceDescriptor descriptor))
            {
                int error =
                    Marshal.GetLastWin32Error();

                throw new IOException(
                    $"WinUsb_QueryInterfaceSettings failed. {DescribeWin32Error(error)}");
            }

            byte? bulkIn =
                null;
            byte? bulkOut =
                null;
            ushort maximumPacketSize =
                0;

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
                    int error =
                        Marshal.GetLastWin32Error();

                    throw new IOException(
                        $"WinUsb_QueryPipe failed for endpoint index {index}. {DescribeWin32Error(error)}");
                }

                if (pipe.PipeType !=
                    UsbdPipeType.Bulk)
                {
                    continue;
                }

                if ((pipe.PipeId & 0x80) != 0)
                {
                    bulkIn ??=
                        pipe.PipeId;
                }
                else
                {
                    bulkOut ??=
                        pipe.PipeId;
                }

                maximumPacketSize =
                    Math.Max(
                        maximumPacketSize,
                        pipe.MaximumPacketSize);
            }

            if (bulkIn is null ||
                bulkOut is null)
            {
                throw new IOException(
                    $"AOA WinUSB interface is missing required bulk endpoints. IN={FormatPipe(bulkIn)}, OUT={FormatPipe(bulkOut)}.");
            }

            SetTransferTimeout(
                winUsbHandle,
                bulkIn.Value,
                timeoutMilliseconds: 1000);

            SetTransferTimeout(
                winUsbHandle,
                bulkOut.Value,
                timeoutMilliseconds: 5000);

            return new NativeWinUsbAoaConnectionOwner(
                deviceHandle,
                winUsbHandle,
                bulkIn.Value,
                bulkOut.Value,
                maximumPacketSize);
        }
        catch
        {
            _ = NativeMethods.WinUsb_Free(
                winUsbHandle);

            deviceHandle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed =
            true;

        if (_winUsbHandle !=
            IntPtr.Zero)
        {
            _ = NativeMethods.WinUsb_AbortPipe(
                _winUsbHandle,
                BulkInPipeId);

            _ = NativeMethods.WinUsb_AbortPipe(
                _winUsbHandle,
                BulkOutPipeId);

            _ = NativeMethods.WinUsb_Free(
                _winUsbHandle);

            _winUsbHandle =
                IntPtr.Zero;
        }

        _deviceHandle.Dispose();

        _readGate.Dispose();
        _writeGate.Dispose();
    }

    private int Read(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateRange(
            buffer.Length,
            offset,
            count);

        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        _readGate.Wait(
            cancellationToken);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(
                    _disposed,
                    this);

                byte[] target =
                    offset == 0 &&
                    count == buffer.Length
                        ? buffer
                        : new byte[count];

                if (NativeMethods.WinUsb_ReadPipe(
                        _winUsbHandle,
                        BulkInPipeId,
                        target,
                        (uint)count,
                        out uint transferred,
                        IntPtr.Zero))
                {
                    if (!ReferenceEquals(
                            target,
                            buffer) &&
                        transferred > 0)
                    {
                        target
                            .AsSpan(
                                0,
                                checked((int)transferred))
                            .CopyTo(
                                buffer.AsSpan(
                                    offset,
                                    checked((int)transferred)));
                    }

                    return checked(
                        (int)transferred);
                }

                int error =
                    Marshal.GetLastWin32Error();

                if (error ==
                    ErrorSemTimeout)
                {
                    continue;
                }

                throw new IOException(
                    $"WinUsb_ReadPipe failed on pipe 0x{BulkInPipeId:X2}. {DescribeWin32Error(error)}");
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void Write(
        ReadOnlySpan<byte> buffer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (buffer.IsEmpty)
        {
            return;
        }

        _writeGate.Wait(
            cancellationToken);

        try
        {
            byte[] bytes =
                buffer.ToArray();

            if (!NativeMethods.WinUsb_WritePipe(
                    _winUsbHandle,
                    BulkOutPipeId,
                    bytes,
                    (uint)bytes.Length,
                    out uint transferred,
                    IntPtr.Zero))
            {
                int error =
                    Marshal.GetLastWin32Error();

                throw new IOException(
                    $"WinUsb_WritePipe failed on pipe 0x{BulkOutPipeId:X2}. {DescribeWin32Error(error)}");
            }

            if (transferred !=
                (uint)bytes.Length)
            {
                throw new IOException(
                    $"WinUsb_WritePipe transferred {transferred} of {bytes.Length} bytes on pipe 0x{BulkOutPipeId:X2}.");
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void SetTransferTimeout(
        IntPtr winUsbHandle,
        byte pipeId,
        uint timeoutMilliseconds)
    {
        uint timeout =
            timeoutMilliseconds;

        if (!NativeMethods.WinUsb_SetPipePolicy(
                winUsbHandle,
                pipeId,
                PipeTransferTimeoutPolicy,
                sizeof(uint),
                ref timeout))
        {
            int error =
                Marshal.GetLastWin32Error();

            throw new IOException(
                $"Could not configure transfer timeout for pipe 0x{pipeId:X2}. {DescribeWin32Error(error)}");
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

    private static string FormatPipe(
        byte? pipeId) =>
        pipeId is null
            ? "missing"
            : $"0x{pipeId.Value:X2}";

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

    private sealed class NativeWinUsbPipeStream : Stream
    {
        private readonly NativeWinUsbAoaConnectionOwner _owner;
        private readonly bool _canRead;
        private readonly bool _canWrite;

        public NativeWinUsbPipeStream(
            NativeWinUsbAoaConnectionOwner owner,
            bool canRead,
            bool canWrite)
        {
            _owner = owner;
            _canRead = canRead;
            _canWrite = canWrite;
        }

        public override bool CanRead =>
            _canRead;

        public override bool CanSeek =>
            false;

        public override bool CanWrite =>
            _canWrite;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            if (!_canRead)
            {
                throw new NotSupportedException();
            }

            return _owner.Read(
                buffer,
                offset,
                count,
                CancellationToken.None);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_canRead)
            {
                throw new NotSupportedException();
            }

            byte[] temporary =
                new byte[buffer.Length];

            int read =
                _owner.Read(
                    temporary,
                    0,
                    temporary.Length,
                    cancellationToken);

            temporary
                .AsMemory(
                    0,
                    read)
                .CopyTo(
                    buffer);

            return ValueTask.FromResult(
                read);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            if (!_canRead)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(
                _owner.Read(
                    buffer,
                    offset,
                    count,
                    cancellationToken));
        }

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
            if (!_canWrite)
            {
                throw new NotSupportedException();
            }

            ValidateRange(
                buffer.Length,
                offset,
                count);

            _owner.Write(
                buffer.AsSpan(
                    offset,
                    count),
                CancellationToken.None);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_canWrite)
            {
                throw new NotSupportedException();
            }

            _owner.Write(
                buffer.Span,
                cancellationToken);

            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            if (!_canWrite)
            {
                throw new NotSupportedException();
            }

            ValidateRange(
                buffer.Length,
                offset,
                count);

            _owner.Write(
                buffer.AsSpan(
                    offset,
                    count),
                cancellationToken);

            return Task.CompletedTask;
        }

        protected override void Dispose(
            bool disposing)
        {
            // Owner lifetime is controlled by WinUsbAoaAccessoryConnection.
            base.Dispose(disposing);
        }
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
        internal static extern bool WinUsb_SetPipePolicy(
            IntPtr interfaceHandle,
            byte pipeId,
            uint policyType,
            uint valueLength,
            ref uint value);

        [DllImport(
            "winusb.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_ReadPipe(
            IntPtr interfaceHandle,
            byte pipeId,
            [Out] byte[] buffer,
            uint bufferLength,
            out uint lengthTransferred,
            IntPtr overlapped);

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
