[CmdletBinding()]
param(
    [string]$InstanceId = "USB\VID_22D9&PID_2764\W4U4SCSSGMIBLJ8H",
    [string]$PackageDirectory,
    [switch]$Install,
    [switch]$IUnderstandThisRestartsTheUsbDevice
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $scriptRoot "artifacts\oppo-a58-test-package"
}
elseif (-not [System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot $PackageDirectory
}

$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$infPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$sysPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.Driver.sys"
$catPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.cat"

foreach ($path in @($manifestPath, $infPath, $sysPath)) {
    if (-not (Test-Path $path)) {
        throw "Required package file is missing: $path"
    }
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json

$expectedHardwareId = "USB\VID_22D9&PID_2764&REV_0404"
if ($manifest.TargetHardwareId -ne $expectedHardwareId) {
    throw "Package manifest target mismatch. Expected '$expectedHardwareId'."
}

if ($manifest.ShippingPackage -ne $false) {
    throw "This harness only accepts the physical-validation package."
}

$actualDriverHash = (Get-FileHash -Path $sysPath -Algorithm SHA256).Hash
$actualInfHash = (Get-FileHash -Path $infPath -Algorithm SHA256).Hash

if ($actualDriverHash -ne $manifest.DriverSha256) {
    throw "Driver SHA-256 does not match package-manifest.json."
}

if ($actualInfHash -ne $manifest.InfSha256) {
    throw "INF SHA-256 does not match package-manifest.json."
}

if ($manifest.InfVerif -ne "passed") {
    throw "Package manifest does not record a passing InfVerif gate."
}

$device = Get-PnpDevice -InstanceId $InstanceId -PresentOnly -ErrorAction Stop
$hardwareIds = @(
    (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_HardwareIds" -ErrorAction Stop).Data
)
$service = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_Service" -ErrorAction Stop).Data
$baseInf = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_DriverInfPath" -ErrorAction Stop).Data

if ($hardwareIds -notcontains $expectedHardwareId) {
    throw "Connected device does not expose the exact validated OPPO A58 hardware revision."
}

if ($device.Class -ne "WPD" -or
    $service -ne "WUDFWpdMtp" -or
    $baseInf -ne "wpdmtp.inf") {
    throw "OPPO A58 is not in the validated no-ADB WPD/MTP state."
}

$adbDevices = @(
    Get-PnpDevice -PresentOnly |
        Where-Object {
            $_.InstanceId -like "USB\VID_22D9&PID_2765*" -or
            $_.FriendlyName -match "(?i)ADB"
        }
)

if ($adbDevices.Count -gt 0) {
    throw "ADB is currently present. Disable USB debugging, unplug/replug the phone, and retry."
}

$driverSignature = Get-AuthenticodeSignature -FilePath $sysPath
$catalogExists = Test-Path $catPath
$catalogSignature =
    if ($catalogExists) {
        Get-AuthenticodeSignature -FilePath $catPath
    }
    else {
        $null
    }

$preflight = [ordered]@{
    TargetStatus = $device.Status
    TargetClass = $device.Class
    TargetHardwareId = $expectedHardwareId
    TargetService = $service
    TargetBaseInf = $baseInf
    PackageDirectory = $PackageDirectory
    ManifestInfVerif = $manifest.InfVerif
    DriverSignatureStatus = $driverSignature.Status.ToString()
    CatalogPresent = $catalogExists
    CatalogSignatureStatus =
        if ($null -eq $catalogSignature) {
            "missing"
        }
        else {
            $catalogSignature.Status.ToString()
        }
    InstallRequested = [bool]$Install
}

$preflight | ConvertTo-Json -Depth 4

if (-not $Install) {
    Write-Host ""
    Write-Host "PRE-FLIGHT ONLY: no driver/package changes were made."
    Write-Host "Use -Install only after the package has been signed and the Windows signing posture has been reviewed."
    exit 0
}

if (-not $IUnderstandThisRestartsTheUsbDevice) {
    throw "Install was requested without -IUnderstandThisRestartsTheUsbDevice."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Physical filter installation requires an elevated PowerShell session."
}

if (-not $catalogExists) {
    throw "Refusing install: package catalog is missing."
}

if ($driverSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Refusing install: kernel driver Authenticode signature is not valid."
}

if ($catalogSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Refusing install: package catalog Authenticode signature is not valid."
}

$baselineScript = Join-Path $repoRoot "windows\eng\capture-usb-bootstrap-baseline.ps1"
$validationDirectory = Join-Path $repoRoot "windows\driver\aoa-bootstrap\artifacts\physical-validation"
New-Item -ItemType Directory -Path $validationDirectory -Force | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$baselinePath = Join-Path $validationDirectory "baseline-$timestamp.json"

& $baselineScript -InstanceId $InstanceId -OutputPath $baselinePath

$pnpUtil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$installOutput = & $pnpUtil /add-driver $infPath /install 2>&1
$installExitCode = $LASTEXITCODE

$installOutput | ForEach-Object { Write-Host $_ }

if ($installExitCode -ne 0) {
    throw "PnPUtil package installation failed with exit code $installExitCode. Baseline is preserved at '$baselinePath'."
}

$publishedNameMatch = [regex]::Match(
    ($installOutput -join [Environment]::NewLine),
    '(?i)\boem\d+\.inf\b')

$publishedName =
    if ($publishedNameMatch.Success) {
        $publishedNameMatch.Value.ToLowerInvariant()
    }
    else {
        $null
    }

Start-Sleep -Seconds 2

$postStack = & $pnpUtil /enum-devices /instanceid $InstanceId /stack /drivers /services 2>&1
$postDevice = Get-PnpDevice -InstanceId $InstanceId -PresentOnly -ErrorAction Stop
$postService = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_Service" -ErrorAction Stop).Data
$postBaseInf = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_DriverInfPath" -ErrorAction Stop).Data
$postCompoundLower = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName "DEVPKEY_Device_CompoundLowerFilters" -ErrorAction SilentlyContinue).Data

$state = [ordered]@{
    InstalledAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    InstanceId = $InstanceId
    PublishedName = $publishedName
    BaselinePath = $baselinePath
    PnpUtilInstallExitCode = $installExitCode
    PostInstall = [ordered]@{
        DeviceStatus = $postDevice.Status
        Service = $postService
        BaseInf = $postBaseInf
        CompoundLowerFilters = $postCompoundLower
        Stack = ($postStack -join [Environment]::NewLine)
    }
}

$statePath = Join-Path $validationDirectory "install-state.json"
$state | ConvertTo-Json -Depth 6 | Set-Content -Path $statePath -Encoding UTF8

if ($postDevice.Status -ne "OK" -or
    $postService -ne "WUDFWpdMtp" -or
    $postBaseInf -ne "wpdmtp.inf") {
    throw "The phone no longer reports the expected healthy WPD/MTP base stack after install. Stop testing and use the recorded install state for rollback."
}

Write-Host ""
Write-Host "Prototype filter package installed for physical validation."
Write-Host "Install state: $statePath"
Write-Host "Do not trigger START_AOA until the post-install stack has been reviewed."
