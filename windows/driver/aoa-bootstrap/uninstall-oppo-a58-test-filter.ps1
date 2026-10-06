[CmdletBinding()]
param(
    [string]$InstanceId = "USB\VID_22D9&PID_2764\W4U4SCSSGMIBLJ8H",
    [string]$StatePath,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $repoRoot "windows\driver\aoa-bootstrap\artifacts\physical-validation\install-state.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($StatePath)) {
    $StatePath = Join-Path $repoRoot $StatePath
}

if (-not (Test-Path $StatePath)) {
    throw "Install state file not found: $StatePath"
}

$state = Get-Content -Path $StatePath -Raw | ConvertFrom-Json
$publishedName = [string]$state.PublishedName

if ($publishedName -notmatch '^oem\d+\.inf$') {
    throw "Install state does not contain a safe published INF name."
}

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\NatsxAoaBootstrap"
$preflight = [ordered]@{
    StatePath = $StatePath
    PublishedName = $publishedName
    BootstrapServicePresent = Test-Path $serviceKey
    UninstallRequested = [bool]$Uninstall
}

$preflight | ConvertTo-Json -Depth 3

if (-not $Uninstall) {
    Write-Host ""
    Write-Host "PRE-FLIGHT ONLY: no package changes were made."
    Write-Host "Use -Uninstall from elevated PowerShell only when rollback is required."
    exit 0
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Prototype package removal requires an elevated PowerShell session."
}

$pnpUtil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$removeOutput = & $pnpUtil /delete-driver $publishedName /uninstall 2>&1
$removeExitCode = $LASTEXITCODE

$removeOutput | ForEach-Object { Write-Host $_ }

if ($removeExitCode -ne 0) {
    throw "PnPUtil package removal failed with exit code $removeExitCode. The script deliberately does not use /force."
}

Start-Sleep -Seconds 2

$result = [ordered]@{
    RemovedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    PublishedName = $publishedName
    PnpUtilExitCode = $removeExitCode
    BootstrapServicePresent = Test-Path $serviceKey
}

$target = Get-PnpDevice -InstanceId $InstanceId -PresentOnly -ErrorAction SilentlyContinue

if ($null -ne $target) {
    $service = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_Service" -ErrorAction SilentlyContinue).Data
    $baseInf = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_DriverInfPath" -ErrorAction SilentlyContinue).Data
    $compoundLower = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_CompoundLowerFilters" -ErrorAction SilentlyContinue).Data
    $stack = & $pnpUtil /enum-devices /instanceid $InstanceId /stack /drivers /services 2>&1

    $result.Target = [ordered]@{
        Status = $target.Status
        Service = $service
        BaseInf = $baseInf
        CompoundLowerFilters = $compoundLower
        Stack = ($stack -join [Environment]::NewLine)
    }

    if ($target.Status -ne "OK" -or
        $service -ne "WUDFWpdMtp" -or
        $baseInf -ne "wpdmtp.inf") {
        throw "Package was removed, but the OPPO A58 has not returned to the expected healthy WPD/MTP state."
    }
}
else {
    $result.Target = "not currently present; unplug/replug in no-ADB File Transfer mode and verify WPD/MTP before continuing"
}

$resultPath = Join-Path (Split-Path -Parent $StatePath) "uninstall-result.json"
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8

Write-Host ""
Write-Host "Prototype package removal completed."
Write-Host "Result: $resultPath"
Write-Host "The script did not use pnputil /force."
