[CmdletBinding()]
param(
    [switch]$Start,
    [switch]$IUnderstandThisReenumeratesThePhone,
    [int]$ReenumerationTimeoutSeconds = 8,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

if ($Start -and -not $IUnderstandThisReenumeratesThePhone) {
    throw "Refusing START_AOA without -IUnderstandThisReenumeratesThePhone."
}

if (-not ("Natsx.AoaBootstrap.StartNativeV8" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Natsx.AoaBootstrap
{
    public static class StartNativeV8
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint IOCTL_GET_VERSION = 0xA3616000;
        private const uint IOCTL_START_AOA = 0xA361E004;
        private const uint IOCTL_PROBE_PROTOCOL_RAW = 0xA361600C;

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

        private static SafeFileHandle Open()
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
                    "Could not open \\.\\NatsxAoaBootstrap.");
            }

            return handle;
        }

        private static byte[] Send(
            SafeFileHandle handle,
            uint ioctl,
            int responseSize,
            string operation)
        {
            byte[] response = new byte[responseSize];
            uint returned = 0;

            if (!DeviceIoControl(
                handle,
                ioctl,
                IntPtr.Zero,
                0,
                response,
                (uint)response.Length,
                out returned,
                IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    operation + " failed.");
            }

            if (returned < responseSize)
            {
                throw new InvalidOperationException(
                    operation + " returned fewer bytes than expected.");
            }

            return response;
        }

        public static byte[] GetVersion()
        {
            using (SafeFileHandle handle = Open())
            {
                return Send(handle, IOCTL_GET_VERSION, 8, "GET_VERSION");
            }
        }

        public static byte[] ProbeRaw()
        {
            using (SafeFileHandle handle = Open())
            {
                return Send(
                    handle,
                    IOCTL_PROBE_PROTOCOL_RAW,
                    24,
                    "AOA_GET_PROTOCOL raw probe");
            }
        }

        public static byte[] StartAoa()
        {
            using (SafeFileHandle handle = Open())
            {
                return Send(handle, IOCTL_START_AOA, 4, "START_AOA");
            }
        }
    }
}
"@
}

$versionBytes = [Natsx.AoaBootstrap.StartNativeV8]::GetVersion()
$probeBytes = [Natsx.AoaBootstrap.StartNativeV8]::ProbeRaw()

$protocolVersion = [BitConverter]::ToUInt32($versionBytes, 0)
$driverBuild = [BitConverter]::ToUInt32($versionBytes, 4)

$probeProtocolVersion = [BitConverter]::ToUInt32($probeBytes, 0)
$probeDriverBuild = [BitConverter]::ToUInt32($probeBytes, 4)
$submitStatus = [BitConverter]::ToUInt32($probeBytes, 8)
$usbStatus = [BitConverter]::ToUInt32($probeBytes, 12)
$bytesTransferred = [BitConverter]::ToUInt32($probeBytes, 16)
$aoaProtocolVersion = [BitConverter]::ToUInt16($probeBytes, 20)
$reserved = [BitConverter]::ToUInt16($probeBytes, 22)

$ready = (
    $protocolVersion -eq 1 -and
    $driverBuild -ge 8 -and
    $probeProtocolVersion -eq $protocolVersion -and
    $probeDriverBuild -eq $driverBuild -and
    $submitStatus -eq 0 -and
    $usbStatus -eq 0 -and
    $bytesTransferred -eq 2 -and
    $aoaProtocolVersion -gt 0 -and
    $reserved -eq 0
)

$report = [ordered]@{
    ControlDevice = "\\.\NatsxAoaBootstrap"
    ProtocolVersion = [uint32]$protocolVersion
    DriverBuild = [uint32]$driverBuild
    RawProbeSubmitStatusHex = ("0x{0:X8}" -f [uint32]$submitStatus)
    RawProbeUsbStatusHex = ("0x{0:X8}" -f [uint32]$usbStatus)
    RawProbeBytesTransferred = [uint32]$bytesTransferred
    AoaProtocolVersion = [uint16]$aoaProtocolVersion
    ReadyForStart = $ready
    StartRequested = [bool]$Start
    StartAoaSent = $false
    Reenumerated = $false
    AoaDevices = @()
}

if (-not $ready) {
    if ($AsJson) {
        $report | ConvertTo-Json -Depth 5
    }
    else {
        $report
    }

    throw "Raw endpoint-zero preflight is not ready; START_AOA was not sent."
}

if (-not $Start) {
    if ($AsJson) {
        $report | ConvertTo-Json -Depth 5
    }
    else {
        $report
    }

    Write-Host ""
    Write-Host "PRE-FLIGHT ONLY: identity strings and START_ACCESSORY were not sent."
    exit 0
}

$startBytes = [Natsx.AoaBootstrap.StartNativeV8]::StartAoa()
$startedProtocol = [BitConverter]::ToUInt16($startBytes, 0)
$startReserved = [BitConverter]::ToUInt16($startBytes, 2)

if ($startReserved -ne 0 -or $startedProtocol -eq 0) {
    throw "START_AOA returned an invalid response."
}

$report.StartAoaSent = $true
$report.AoaProtocolVersion = [uint16]$startedProtocol

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReenumerationTimeoutSeconds)

do {
    Start-Sleep -Milliseconds 100

    $aoaDevices = @(
        Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
            Where-Object {
                $_.InstanceId -like "USB\VID_18D1&PID_2D00*" -or
                $_.InstanceId -like "USB\VID_18D1&PID_2D01*"
            } |
            Select-Object Status, Class, FriendlyName, InstanceId
    )

    if ($aoaDevices.Count -gt 0) {
        $report.Reenumerated = $true
        $report.AoaDevices = $aoaDevices
        break
    }
}
while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($AsJson) {
    $report | ConvertTo-Json -Depth 5
}
else {
    $report
}

if (-not $report.Reenumerated) {
    exit 3
}
