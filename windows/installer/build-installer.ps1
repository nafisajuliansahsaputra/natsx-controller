param(
    [string]$PublishDir = "artifacts/windows-receiver",
    [string]$OutputDir = "artifacts/installer",
    [string]$DriverRoot = "artifacts/windows-drivers-production",
    [switch]$AllowMissingProductionUsbDrivers
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$version = (Get-Content (Join-Path $repoRoot "VERSION") -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION is empty."
}

$publishPath = (Resolve-Path (Join-Path $repoRoot $PublishDir)).Path
$outputPath = Join-Path $repoRoot $OutputDir
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$requiredPublishedFiles = @(
    "Natsx.Controller.Receiver.exe",
    "HIDMaestro.Core.dll",
    "THIRD_PARTY_NOTICES.md",
    "licenses\HIDMaestro.LICENSE.txt"
)

foreach ($requiredFile in $requiredPublishedFiles) {
    $requiredPath = Join-Path $publishPath $requiredFile

    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required published installer input is missing: $requiredPath"
    }
}

$driverRootPath =
    if ([System.IO.Path]::IsPathRooted($DriverRoot)) {
        $DriverRoot
    }
    else {
        Join-Path $repoRoot $DriverRoot
    }

$bootstrapDriverDir = Join-Path $driverRootPath "aoa-bootstrap"
$winUsbDriverDir = Join-Path $driverRootPath "aoa-winusb"
$hasBootstrapDrivers = Test-Path -LiteralPath $bootstrapDriverDir -PathType Container
$hasWinUsbDrivers = Test-Path -LiteralPath $winUsbDriverDir -PathType Container

if ($hasBootstrapDrivers -xor $hasWinUsbDrivers) {
    throw "Production USB driver bundle is incomplete. Both aoa-bootstrap and aoa-winusb directories are required."
}

$includeProductionUsbDrivers =
    $hasBootstrapDrivers -and
    $hasWinUsbDrivers

if (-not $includeProductionUsbDrivers -and -not $AllowMissingProductionUsbDrivers) {
    throw (
        "Production USB driver bundle is missing at '$driverRootPath'. " +
        "Feature-complete release installers require Microsoft retail-signed USB drivers. " +
        "See docs/release/windows-production-driver.md. " +
        "Use -AllowMissingProductionUsbDrivers only for non-shipping CI installer compilation."
    )
}

if ($includeProductionUsbDrivers) {
    $validator = Join-Path $repoRoot "windows\driver\validate-production-packages.ps1"
    & $validator -BootstrapPackageDirectory $bootstrapDriverDir -WinUsbPackageDirectory $winUsbDriverDir
}

$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $programFilesX86 =
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFilesX86)

    $candidate =
        Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"

    if (Test-Path $candidate) {
        $iscc = Get-Item $candidate
    }
}

if ($null -eq $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found."
}

$script = Join-Path $PSScriptRoot "NatsxController.iss"
$isccArguments = @(
    "/DMyAppVersion=$version",
    "/DPublishDir=$publishPath",
    "/O$outputPath"
)

if ($includeProductionUsbDrivers) {
    $isccArguments += "/DIncludeUsbDrivers=1"
    $isccArguments += "/DBootstrapDriverDir=$bootstrapDriverDir"
    $isccArguments += "/DWinUsbDriverDir=$winUsbDriverDir"
}
else {
    $isccArguments += "/DIncludeUsbDrivers=0"
    Write-Warning "Compiling a NON-SHIPPING installer without NATSX USB drivers."
}

& $iscc.Source @isccArguments $script

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created in $outputPath"
