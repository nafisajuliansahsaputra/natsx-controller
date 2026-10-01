[CmdletBinding()]
param(
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$secureBootState = "unknown"
$secureBootValue = $null
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
    SecureBootStillEnabled = ($secureBootState -eq "enabled")
    TestSigningStillOff = -not $testSigningOn
    VbsStillRunning = ($deviceGuard.VirtualizationBasedSecurityStatus -eq 2)
    HvciStillConfiguredAndRunning = (
        $hvciEnabled -eq 1 -and
        (@($deviceGuard.SecurityServicesRunning) -contains 2)
    )
    BitLockerProtectionSuspended = ($bitLockerProtection -match "(?i)Off")
    BitLockerStillFullyEncrypted = (
        $bitLocker.EncryptionPercentage -eq 100 -and
        $bitLockerVolumeStatus -match "(?i)FullyEncrypted"
    )
}

$ready = -not (@($checks.Values) -contains $false)

$report = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    ReadyForSecureBootDisableGate = $ready
    Checks = $checks
    Current = [ordered]@{
        SecureBoot = $secureBootState
        TestSigningOn = $testSigningOn
        VbsStatus = $deviceGuard.VirtualizationBasedSecurityStatus
        HvciRegistryEnabled = $hvciEnabled
        BitLockerProtectionStatus = $bitLockerProtection
        BitLockerVolumeStatus = $bitLockerVolumeStatus
        BitLockerEncryptionPercentage = $bitLocker.EncryptionPercentage
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
