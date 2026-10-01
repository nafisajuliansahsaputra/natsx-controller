[CmdletBinding()]
param(
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

if (-not ("Natsx.AoaBootstrap.SidebandProbeNative" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Natsx.AoaBootstrap
{
    public static class SidebandProbeNative
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint IOCTL_NATSX_GET_VERSION = 0xA3616000;
        private const uint IOCTL_NATSX_GET_STATUS = 0xA3616008;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private static SafeFileHandle OpenControlDevice()
        {
            SafeFileHandle handle = CreateFileW(
                @"\\.\NatsxAoaBootstrap",
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(
                    error,
                    "Could not open \\.\\NatsxAoaBootstrap (Win32 error " + error + ").");
            }

            return handle;
        }

        private static byte[] SendReadOnlyIoctl(
            SafeFileHandle handle,
            uint controlCode,
            int responseSize,
            string operation)
        {
            byte[] response = new byte[responseSize];
            uint bytesReturned = 0;

            if (!DeviceIoControl(
                handle,
                controlCode,
                IntPtr.Zero,
                0,
                response,
                (uint)response.Length,
                out bytesReturned,
                IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    operation + " failed (Win32 error " + error + ").");
            }

            if (bytesReturned < responseSize)
            {
                throw new InvalidOperationException(
                    operation + " returned fewer bytes than expected.");
            }

            return response;
        }

        public static uint[] GetVersion()
        {
            using (SafeFileHandle handle = OpenControlDevice())
            {
                byte[] response = SendReadOnlyIoctl(
                    handle,
                    IOCTL_NATSX_GET_VERSION,
                    8,
                    "NATSX bootstrap GET_VERSION");

                return new uint[]
                {
                    BitConverter.ToUInt32(response, 0),
                    BitConverter.ToUInt32(response, 4)
                };
            }
        }

        public static uint[] GetStatus()
        {
            using (SafeFileHandle handle = OpenControlDevice())
            {
                byte[] response = SendReadOnlyIoctl(
                    handle,
                    IOCTL_NATSX_GET_STATUS,
                    16,
                    "NATSX bootstrap GET_STATUS");

                return new uint[]
                {
                    BitConverter.ToUInt32(response, 0),
                    BitConverter.ToUInt32(response, 4),
                    BitConverter.ToUInt32(response, 8),
                    BitConverter.ToUInt32(response, 12)
                };
            }
        }


    }
}
"@
}

$version = [Natsx.AoaBootstrap.SidebandProbeNative]::GetVersion()
$status = [Natsx.AoaBootstrap.SidebandProbeNative]::GetStatus()

$report = [ordered]@{
    ControlDevice = "\\.\NatsxAoaBootstrap"
    ReadOnlyIoctls = @("GET_VERSION", "GET_STATUS")
    StartAoaSent = $false
    ProtocolVersion = [uint32]$version[0]
    DriverBuild = [uint32]$version[1]
    AttachedTargetCount = [uint32]$status[2]
    ReadyUsbTargetCount = [uint32]$status[3]
    Compatible = (
        [uint32]$version[0] -eq 1 -and
        [uint32]$version[1] -ge 4 -and
        [uint32]$status[0] -eq [uint32]$version[0] -and
        [uint32]$status[1] -eq [uint32]$version[1]
    )
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 3
}
else {
    $report
}

if (-not $report.Compatible) {
    exit 2
}
