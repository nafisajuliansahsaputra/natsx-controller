[CmdletBinding()]
param(
    [string]$Version = "v1.9.2",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$ExpectedVersion = "v1.9.2"
$ExpectedSha256 = "1c19e9a34360652a8f98c595ff6c03c1f1fd4c027f180cc4d1149e3d352643d7"

if ($Version -ne $ExpectedVersion) {
    throw "Unsupported HIDMaestro version '$Version'. Update this script and ADR 0002 intentionally before changing versions."
}

$WindowsRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = [System.IO.Path]::Combine($WindowsRoot, "lib", "HIDMaestro")
$DestinationDll = Join-Path $OutputDir "HIDMaestro.Core.dll"
$DestinationLicense = Join-Path $OutputDir "HIDMaestro.LICENSE.txt"

if (
    (Test-Path $DestinationDll) -and
    (Test-Path $DestinationLicense) -and
    -not $Force
) {
    Write-Host "HIDMaestro SDK and license already present: $OutputDir"
    exit 0
}

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("natsx-hidmaestro-" + [guid]::NewGuid().ToString("N"))
$ZipPath = Join-Path $TempRoot "hidmaestro.zip"
$ExtractDir = Join-Path $TempRoot "extract"
$Uri = "https://github.com/hifihedgehog/HIDMaestro/releases/download/$Version/HIDMaestro-$Version.zip"

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $ExtractDir | Out-Null
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    Write-Host "Downloading HIDMaestro $Version..."
    Invoke-WebRequest -Uri $Uri -OutFile $ZipPath -UseBasicParsing

    $Sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $ArchiveStream = [System.IO.File]::OpenRead($ZipPath)
        try {
            $HashBytes = $Sha256.ComputeHash($ArchiveStream)
        }
        finally {
            $ArchiveStream.Dispose()
        }
    }
    finally {
        $Sha256.Dispose()
    }

    $ActualSha256 =
        ([System.BitConverter]::ToString($HashBytes) -replace "-", "").ToLowerInvariant()

    if ($ActualSha256 -ne $ExpectedSha256) {
        throw "HIDMaestro archive SHA-256 mismatch. Expected $ExpectedSha256, got $ActualSha256."
    }

    Write-Host "SHA-256 verified."
    Expand-Archive -Path $ZipPath -DestinationPath $ExtractDir -Force

    $SdkDll = Get-ChildItem -Path $ExtractDir -Filter "HIDMaestro.Core.dll" -File -Recurse |
        Select-Object -First 1

    if ($null -eq $SdkDll) {
        throw "HIDMaestro.Core.dll was not found in the pinned release archive."
    }

    $LicenseFile = Get-ChildItem -Path $ExtractDir -Filter "LICENSE*" -File -Recurse |
        Select-Object -First 1

    if ($null -eq $LicenseFile) {
        throw "Pinned HIDMaestro archive did not contain a LICENSE file."
    }

    Copy-Item -Path $SdkDll.FullName -Destination $DestinationDll -Force
    Copy-Item -Path $LicenseFile.FullName -Destination $DestinationLicense -Force

    Write-Host "HIDMaestro SDK ready: $DestinationDll"
    Write-Host "HIDMaestro license ready: $DestinationLicense"
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item -Path $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
