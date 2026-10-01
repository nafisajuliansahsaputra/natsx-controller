[CmdletBinding()]
param(
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Get-RegistryDword {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    try {
        return (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name
    }
    catch {
        return $null
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$secureBoot = [ordered]@{
    State = "unknown"
    Value = $null
    Detail = $null
}

if (Get-Command Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) {
    try {
        $secureBoot.Value = Confirm-SecureBootUEFI
        $secureBoot.State = if ($secureBoot.Value) { "enabled" } else { "disabled" }
    }
    catch [System.UnauthorizedAccessException] {
        $secureBoot.State = "requires-elevation"
        $secureBoot.Detail = $_.Exception.Message
    }
    catch {
        $secureBoot.State = "unavailable"
        $secureBoot.Detail = $_.Exception.Message
    }
}
else {
    $secureBoot.State = "cmdlet-unavailable"
}

$deviceGuard = $null
try {
    $deviceGuard = Get-CimInstance `
        -Namespace "root\Microsoft\Windows\DeviceGuard" `
        -ClassName "Win32_DeviceGuard" `
        -ErrorAction Stop |
        Select-Object `
            VirtualizationBasedSecurityStatus, `
            SecurityServicesConfigured, `
            SecurityServicesRunning, `
            CodeIntegrityPolicyEnforcementStatus, `
            UsermodeCodeIntegrityPolicyEnforcementStatus
}
catch {
    $deviceGuard = [ordered]@{ Error = $_.Exception.Message }
}

$hvciPath = "HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"
$hvci = [ordered]@{
    Enabled = Get-RegistryDword -Path $hvciPath -Name "Enabled"
    Locked = Get-RegistryDword -Path $hvciPath -Name "Locked"
}

$bcdEdit = Join-Path $env:SystemRoot "System32\bcdedit.exe"
$bcdCurrent = $null
if (Test-Path $bcdEdit) {
    try {
        # Enumerating all entries avoids shell/parser edge cases around the
        # special {current} identifier and is sufficient for read-only
        # TESTSIGNING posture inspection.
        $bcdOutput = & $bcdEdit /enum all 2>&1
        $bcdText = $bcdOutput -join [Environment]::NewLine
        $bcdCurrent = [ordered]@{
            ExitCode = $LASTEXITCODE
            Raw = $bcdText
            TestSigningMentioned = [bool]($bcdText -match "(?im)^\s*testsigning\s+")
            TestSigningOn = [bool]($bcdText -match "(?im)^\s*testsigning\s+(yes|on|true)\s*$")
        }
    }
    catch {
        $bcdCurrent = [ordered]@{ Error = $_.Exception.Message }
    }
}

$bitLocker = $null
if (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) {
    try {
        $bitLocker = @(
            Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop |
                Select-Object MountPoint, VolumeStatus, ProtectionStatus, EncryptionPercentage, KeyProtector
        )
    }
    catch {
        $bitLocker = [ordered]@{ Error = $_.Exception.Message }
    }
}

$report = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    NeedsElevationForCompleteResult = -not $isAdmin
    IsAdministrator = $isAdmin
    WindowsVersion = [System.Environment]::OSVersion.Version.ToString()
    SecureBoot = $secureBoot
    DeviceGuard = $deviceGuard
    HVCIRegistry = $hvci
    BcdCurrent = $bcdCurrent
    SystemDriveBitLocker = $bitLocker
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 8
}
else {
    $report
}
