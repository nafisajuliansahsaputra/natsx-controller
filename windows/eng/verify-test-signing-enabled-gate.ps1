[CmdletBinding()]
param(
    [string]$InstanceId,
    [string]$PackageDirectory,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Resolve-ExactTargetDevice {
    param(
        [string]$RequestedInstanceId,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedHardwareId
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedInstanceId)) {
        $requested = Get-PnpDevice -InstanceId $RequestedInstanceId -PresentOnly -ErrorAction SilentlyContinue
        if ($null -eq $requested) {
            throw "Requested OPPO A58 instance is not currently present: $RequestedInstanceId"
        }

        $requestedHardwareIds = @(
            (Get-PnpDeviceProperty -InstanceId $requested.InstanceId -KeyName "DEVPKEY_Device_HardwareIds" -ErrorAction Stop).Data
        )

        if ($requestedHardwareIds -notcontains $ExpectedHardwareId) {
            throw "Requested device does not expose the exact validated hardware ID '$ExpectedHardwareId'."
        }

        return $requested
    }

    $candidates = @(
        Get-PnpDevice -PresentOnly -ErrorAction Stop |
            Where-Object { $_.InstanceId -like "USB\VID_22D9&PID_2764*" }
    )

    $exactMatches = @(
        foreach ($candidate in $candidates) {
            try {
                $ids = @(
                    (Get-PnpDeviceProperty -InstanceId $candidate.InstanceId -KeyName "DEVPKEY_Device_HardwareIds" -ErrorAction Stop).Data
                )

                if ($ids -contains $ExpectedHardwareId) {
                    $candidate
                }
            }
            catch {
                # Ignore transient candidate/property failures and continue exact matching.
            }
        }
    )

    if ($exactMatches.Count -eq 0) {
        throw "No present OPPO A58 exposes '$ExpectedHardwareId'. Reconnect the phone with USB debugging/ADB disabled and USB mode set to File Transfer, then retry."
    }

    if ($exactMatches.Count -gt 1) {
        $ids = ($exactMatches | ForEach-Object { $_.InstanceId }) -join "; "
        throw "Multiple exact OPPO A58 targets are present; pass -InstanceId explicitly. Matches: $ids"
    }

    return $exactMatches[0]
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    throw "-PackageDirectory is required and must point to the extracted CI validation package."
}
elseif (-not [System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot $PackageDirectory
}

$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$sysPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.Driver.sys"
$catPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.cat"

foreach ($path in @($manifestPath, $sysPath, $catPath)) {
    if (-not (Test-Path $path)) {
        throw "Required validation artifact is missing: $path"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$expectedHardwareId = "USB\VID_22D9&PID_2764&REV_0404"
$expectedThumbprint = ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

$device = Resolve-ExactTargetDevice -RequestedInstanceId $InstanceId -ExpectedHardwareId $expectedHardwareId
$InstanceId = $device.InstanceId

$hardwareIds = @(
    (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_HardwareIds" -ErrorAction Stop).Data
)
$service = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_Service" -ErrorAction Stop).Data
$baseInf = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_DriverInfPath" -ErrorAction Stop).Data

$adbPresent = @(
    Get-PnpDevice -PresentOnly |
        Where-Object {
            $_.InstanceId -like "USB\VID_22D9&PID_2765*" -or
            $_.FriendlyName -match "(?i)ADB"
        }
).Count -gt 0

$driverSignature = Get-AuthenticodeSignature -FilePath $sysPath
$catalogSignature = Get-AuthenticodeSignature -FilePath $catPath

$driverSigner =
    if ($null -ne $driverSignature.SignerCertificate) {
        $driverSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

$catalogSigner =
    if ($null -ne $catalogSignature.SignerCertificate) {
        $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

$rootTrusted = Test-Path ("Cert:\LocalMachine\Root\" + $expectedThumbprint)
$trustedPublisher = Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $expectedThumbprint)

$secureBootState = "unknown"
try {
    $secureBootValue = Confirm-SecureBootUEFI
    $secureBootState = if ($secureBootValue) { "enabled" } else { "disabled" }
}
catch [System.UnauthorizedAccessException] {
    $secureBootState = "requires-elevation"
}
catch {
    $secureBootState = "unavailable"
}

$deviceGuard = Get-CimInstance `
    -Namespace "root\Microsoft\Windows\DeviceGuard" `
    -ClassName "Win32_DeviceGuard" `
    -ErrorAction Stop

$hvciEnabled = $null
try {
    $hvciEnabled = (Get-ItemProperty `
        -Path "HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity" `
        -Name "Enabled" `
        -ErrorAction Stop).Enabled
}
catch {
    $hvciEnabled = $null
}

$bcdText = (& "$env:SystemRoot\System32\bcdedit.exe" /enum all 2>&1) -join [Environment]::NewLine
$testSigningOn = [bool]($bcdText -match "(?im)^\s*testsigning\s+(yes|on|true)\s*$")

$bitLocker = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
$bitLockerProtection = $bitLocker.ProtectionStatus.ToString()
$bitLockerVolumeStatus = $bitLocker.VolumeStatus.ToString()

$checks = [ordered]@{
    Administrator = $isAdmin
    SecureBootDisabled = ($secureBootState -eq "disabled")
    TestSigningOn = $testSigningOn
    VbsStillRunning = ($deviceGuard.VirtualizationBasedSecurityStatus -eq 2)
    HvciStillConfiguredAndRunning = (
        $hvciEnabled -eq 1 -and
        (@($deviceGuard.SecurityServicesRunning) -contains 2)
    )
    BitLockerProtectionStillSuspended = ($bitLockerProtection -match "(?i)Off")
    BitLockerStillFullyEncrypted = (
        $bitLocker.EncryptionPercentage -eq 100 -and
        $bitLockerVolumeStatus -match "(?i)FullyEncrypted"
    )
    ExactTargetHardware = $hardwareIds -contains $expectedHardwareId
    TargetHealthyWpdMtp = ($device.Status -eq "OK" -and $device.Class -eq "WPD" -and $service -eq "WUDFWpdMtp" -and $baseInf -eq "wpdmtp.inf")
    AdbAbsent = -not $adbPresent
    DriverSignatureValid = ($driverSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid)
    CatalogSignatureValid = ($catalogSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid)
    DriverSignerMatchesManifest = ($driverSigner -eq $expectedThumbprint)
    CatalogSignerMatchesManifest = ($catalogSigner -eq $expectedThumbprint)
    RootTrusted = $rootTrusted
    TrustedPublisher = $trustedPublisher
}

$ready = -not (@($checks.Values) -contains $false)

$report = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    ReadyForPhysicalFilterInstallGate = $ready
    Checks = $checks
    Current = [ordered]@{
        SecureBoot = $secureBootState
        TestSigningOn = $testSigningOn
        VbsStatus = $deviceGuard.VirtualizationBasedSecurityStatus
        HvciRegistryEnabled = $hvciEnabled
        BitLockerProtectionStatus = $bitLockerProtection
        BitLockerVolumeStatus = $bitLockerVolumeStatus
        BitLockerEncryptionPercentage = $bitLocker.EncryptionPercentage
        DeviceInstanceId = $InstanceId
        DeviceStatus = $device.Status
        DeviceService = $service
        DeviceBaseInf = $baseInf
    }
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 6
}
else {
    $report
}

if (-not $ready) {
    exit 2
}
