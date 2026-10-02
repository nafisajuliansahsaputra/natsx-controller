[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [switch]$Stage
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $scriptRoot "artifacts\test-package"
}

$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$infPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.inf"
$catPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.cat"

foreach ($path in @($manifestPath, $infPath, $catPath)) {
    if (-not (Test-Path $path)) { throw "Required AOA WinUSB package file missing: $path" }
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$catalogSignature = Get-AuthenticodeSignature $catPath
$thumbprint = ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

$rootTrusted = Test-Path ("Cert:\LocalMachine\Root\" + $thumbprint)
$publisherTrusted = Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $thumbprint)

$report = [ordered]@{
    PackageDirectory = $PackageDirectory
    InfVerif = $manifest.InfVerif
    SupportedHardwareIds = $manifest.SupportedHardwareIds
    DeviceInterfaceGuid = $manifest.DeviceInterfaceGuid
    CatalogSignatureStatus = $catalogSignature.Status.ToString()
    RootTrusted = $rootTrusted
    TrustedPublisher = $publisherTrusted
    StageRequested = [bool]$Stage
}
$report | ConvertTo-Json -Depth 4

if (-not $Stage) {
    Write-Host "PRE-FLIGHT ONLY: AOA WinUSB package was not staged."
    exit 0
}

if ($catalogSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    -not $rootTrusted -or -not $publisherTrusted -or
    $manifest.InfVerif -ne "passed") {
    throw "AOA WinUSB package is not ready to stage."
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Staging AOA WinUSB package requires elevated PowerShell."
}

& "$env:SystemRoot\System32\pnputil.exe" /add-driver $infPath
if ($LASTEXITCODE -ne 0) {
    throw "PnPUtil failed to stage AOA WinUSB package."
}

Write-Host "AOA WinUSB package staged in the Windows driver store."
