[CmdletBinding()]
param(
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

if (-not ("Natsx.AoaBootstrap.RawProtocolProbeNativeV6" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Natsx.AoaBootstrap
{
    public static class RawProtocolProbeNativeV6
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_NATSX_PROBE_PROTOCOL_RAW = 0xA361600C;

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

        public static byte[] Probe()
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
                    throw new Win32Exception(
                        error,
                        "Could not open \\.\\NatsxAoaBootstrap (Win32 error " + error + ").");
                }

                byte[] response = new byte[24];
                uint bytesReturned = 0;

                if (!DeviceIoControl(
                    handle,
                    IOCTL_NATSX_PROBE_PROTOCOL_RAW,
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
                        "NATSX raw AOA protocol probe failed (Win32 error " + error + ").");
                }

                if (bytesReturned < response.Length)
                {
                    throw new InvalidOperationException(
                        "NATSX raw AOA protocol probe returned fewer than 24 bytes.");
                }

                return response;
            }
        }
    }
}
"@
}

$response = [Natsx.AoaBootstrap.RawProtocolProbeNativeV6]::Probe()

$protocolVersion = [BitConverter]::ToUInt32($response, 0)
$driverBuild = [BitConverter]::ToUInt32($response, 4)
$submitStatus = [BitConverter]::ToUInt32($response, 8)
$usbStatus = [BitConverter]::ToUInt32($response, 12)
$bytesTransferred = [BitConverter]::ToUInt32($response, 16)
$aoaProtocolVersion = [BitConverter]::ToUInt16($response, 20)
$reserved = [BitConverter]::ToUInt16($response, 22)

$rawEndpointZeroUsable = (
    $submitStatus -eq 0 -and
    $usbStatus -eq 0 -and
    $bytesTransferred -eq 2 -and
    $aoaProtocolVersion -gt 0 -and
    $reserved -eq 0
)

$report = [ordered]@{
    ControlDevice = "\\.\NatsxAoaBootstrap"
    ReadOnlyRequest = "AOA_GET_PROTOCOL"
    StartAoaSent = $false
    IdentityStringsSent = $false
    ProtocolVersion = [uint32]$protocolVersion
    DriverBuild = [uint32]$driverBuild
    RawUrbSubmitStatusHex = ("0x{0:X8}" -f [uint32]$submitStatus)
    RawUrbUsbStatusHex = ("0x{0:X8}" -f [uint32]$usbStatus)
    BytesTransferred = [uint32]$bytesTransferred
    AoaProtocolVersion = [uint16]$aoaProtocolVersion
    Reserved = [uint16]$reserved
    RawEndpointZeroUsable = $rawEndpointZeroUsable
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 3
}
else {
    $report
}

if (-not $rawEndpointZeroUsable) {
    exit 2
}
