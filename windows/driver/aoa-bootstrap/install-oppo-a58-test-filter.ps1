[CmdletBinding()]
param(
    [string]$InstanceId,
    [string]$PackageDirectory,
    [switch]$Install,
    [switch]$IUnderstandThisRestartsTheUsbDevice
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
$actualCatalogHash =
    if (Test-Path $catPath) {
        (Get-FileHash -Path $catPath -Algorithm SHA256).Hash
    }
    else {
        $null
    }

if ($actualDriverHash -ne $manifest.DriverSha256) {
    throw "Driver SHA-256 does not match package-manifest.json."
}

if ($actualInfHash -ne $manifest.InfSha256) {
    throw "INF SHA-256 does not match package-manifest.json."
}

if ($manifest.InfVerif -ne "passed") {
    throw "Package manifest does not record a passing InfVerif gate."
}

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Package manifest does not identify the expected ephemeral CI test-signing mode."
}

if ([string]::IsNullOrWhiteSpace([string]$manifest.TestCertificateThumbprint)) {
    throw "Package manifest does not contain the CI test-certificate thumbprint."
}

if (-not [string]::IsNullOrWhiteSpace([string]$manifest.CatalogSha256) -and
    $actualCatalogHash -ne $manifest.CatalogSha256) {
    throw "Catalog SHA-256 does not match package-manifest.json."
}

$device = Resolve-ExactTargetDevice -RequestedInstanceId $InstanceId -ExpectedHardwareId $expectedHardwareId
$InstanceId = $device.InstanceId

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

$expectedSignerThumbprint =
    ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

$driverSignerThumbprint =
    if ($null -ne $driverSignature.SignerCertificate) {
        $driverSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

$catalogSignerThumbprint =
    if ($null -ne $catalogSignature -and
        $null -ne $catalogSignature.SignerCertificate) {
        $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

$driverSignerMatchesManifest =
    $driverSignerThumbprint -eq $expectedSignerThumbprint

$catalogSignerMatchesManifest =
    $catalogSignerThumbprint -eq $expectedSignerThumbprint

$rootTrusted =
    Test-Path ("Cert:\LocalMachine\Root\" + $expectedSignerThumbprint)

$trustedPublisher =
    Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $expectedSignerThumbprint)

$preflight = [ordered]@{
    TargetInstanceId = $InstanceId
    TargetStatus = $device.Status
    TargetClass = $device.Class
    TargetHardwareId = $expectedHardwareId
    TargetService = $service
    TargetBaseInf = $baseInf
    PackageDirectory = $PackageDirectory
    ManifestInfVerif = $manifest.InfVerif
    DriverSignatureStatus = $driverSignature.Status.ToString()
    DriverSignatureStatusMessage = $driverSignature.StatusMessage
    DriverSignerSubject =
        if ($null -eq $driverSignature.SignerCertificate) {
            $null
        }
        else {
            $driverSignature.SignerCertificate.Subject
        }
    DriverSignerThumbprint = $driverSignerThumbprint
    DriverSignerMatchesManifest = $driverSignerMatchesManifest
    CatalogPresent = $catalogExists
    CatalogSignatureStatus =
        if ($null -eq $catalogSignature) {
            "missing"
        }
        else {
            $catalogSignature.Status.ToString()
        }
    CatalogSignatureStatusMessage =
        if ($null -eq $catalogSignature) {
            $null
        }
        else {
            $catalogSignature.StatusMessage
        }
    CatalogSignerSubject =
        if ($null -eq $catalogSignature -or
            $null -eq $catalogSignature.SignerCertificate) {
            $null
        }
        else {
            $catalogSignature.SignerCertificate.Subject
        }
    CatalogSignerThumbprint = $catalogSignerThumbprint
    CatalogSignerMatchesManifest = $catalogSignerMatchesManifest
    RootTrusted = $rootTrusted
    TrustedPublisher = $trustedPublisher
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

if (-not $driverSignerMatchesManifest) {
    throw "Refusing install: kernel driver signer does not match package-manifest.json."
}

if (-not $catalogSignerMatchesManifest) {
    throw "Refusing install: package catalog signer does not match package-manifest.json."
}

if (-not $rootTrusted -or -not $trustedPublisher) {
    throw "Refusing install: the exact manifest test certificate is not trusted in both required LocalMachine stores."
}

if ($driverSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Refusing install: kernel driver Authenticode signature is not valid after trust."
}

if ($catalogSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Refusing install: package catalog Authenticode signature is not valid after trust."
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
