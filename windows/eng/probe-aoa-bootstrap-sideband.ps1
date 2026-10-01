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

        public static uint[] GetVersion()
        {
            using (SafeFileHandle handle = CreateFileW(
                @"\\.\NatsxAoaBootstrap",
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Could not open \\.\\NatsxAoaBootstrap.");
                }

                byte[] response = new byte[8];

                if (!DeviceIoControl(
                    handle,
                    IOCTL_NATSX_GET_VERSION,
                    IntPtr.Zero,
                    0,
                    response,
                    (uint)response.Length,
                    out uint bytesReturned,
                    IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "NATSX bootstrap GET_VERSION failed.");
                }

                if (bytesReturned < 8)
                {
                    throw new InvalidOperationException(
                        "NATSX bootstrap GET_VERSION returned fewer than 8 bytes.");
                }

                return new uint[]
                {
                    BitConverter.ToUInt32(response, 0),
                    BitConverter.ToUInt32(response, 4)
                };
            }
        }
    }
}
"@
}

$version = [Natsx.AoaBootstrap.SidebandProbeNative]::GetVersion()

$report = [ordered]@{
    ControlDevice = "\\.\NatsxAoaBootstrap"
    GetVersionOnly = $true
    StartAoaSent = $false
    ProtocolVersion = [uint32]$version[0]
    DriverBuild = [uint32]$version[1]
    Compatible = (
        [uint32]$version[0] -eq 1 -and
        [uint32]$version[1] -ge 3
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
