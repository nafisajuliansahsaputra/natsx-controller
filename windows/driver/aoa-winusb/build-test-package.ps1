[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipInfVerif
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptRoot "artifacts\test-package"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$sourceInf = Join-Path $scriptRoot "NatsxAoaWinUsb.inf"
$packageInf = Join-Path $OutputDirectory "NatsxAoaWinUsb.inf"

if (-not (Test-Path $sourceInf)) {
    throw "AOA WinUSB INF not found: $sourceInf"
}

$inf = Get-Content -Path $sourceInf -Raw

$requiredIds = @(
    "USB\VID_18D1&PID_2D00",
    "USB\VID_18D1&PID_2D01&MI_00"
)

foreach ($id in $requiredIds) {
    if ($inf -notmatch [regex]::Escape($id)) {
        throw "AOA WinUSB INF is missing required exact target '$id'."
    }
}

if ($inf -match '(?im)^.*USB\VID_18D1&PID_2D01\s*$') {
    throw "Refusing package: PID_2D01 must target MI_00 only."
}

if ($inf -match '(?im)USB\Class_|DefaultInstall|ClassInstall32') {
    throw "Refusing package: unexpected broad or legacy install directive."
}

$requiredGuid = "{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}"
if ($inf -notmatch [regex]::Escape($requiredGuid)) {
    throw "AOA WinUSB INF is missing the NATSX device interface GUID."
}

if ($inf -notmatch '(?im)^Include\s*=\s*winusb\.inf\s*$' -or
    $inf -notmatch '(?im)^Needs\s*=\s*WINUSB\.NT\s*$') {
    throw "AOA WinUSB INF no longer delegates to Microsoft's inbox WinUSB driver."
}

if (Test-Path $OutputDirectory) {
    Remove-Item -Path $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -Path $sourceInf -Destination $packageInf -Force

function Find-WdkTool {
    param([Parameter(Mandatory = $true)][string]$Name)

    $roots = @(
        (Join-Path $scriptRoot "..\aoa-bootstrap\packages"),
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin")
    ) | Where-Object { Test-Path $_ }

    $matches = @(
        Get-ChildItem -Path $roots -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '(?i)[\\/]x64(?:[\\/]|$)' } |
            Sort-Object FullName -Descending
    )

    if ($matches.Count -eq 0) { return $null }
    return $matches[0].FullName
}

if (-not $SkipInfVerif) {
    $infVerif = Find-WdkTool -Name "infverif.exe"
    if ([string]::IsNullOrWhiteSpace($infVerif)) {
        throw "infverif.exe was not found."
    }

    & $infVerif /w /v $packageInf
    if ($LASTEXITCODE -ne 0) {
        throw "InfVerif failed with exit code $LASTEXITCODE."
    }
}

$manifest = [ordered]@{
    PackageKind = "NATSX AOA post-accessory WinUSB physical-validation package"
    ShippingPackage = $false
    SupportedHardwareIds = $requiredIds
    DeviceInterfaceGuid = $requiredGuid
    UsesInboxWinUsb = $true
    ADBRequired = $false
    UsbTetheringRequired = $false
    InfSha256 = (Get-FileHash -Path $packageInf -Algorithm SHA256).Hash
    InfVerif = if ($SkipInfVerif) { "skipped" } else { "passed" }
}

$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -Path (Join-Path $OutputDirectory "package-manifest.json") -Encoding UTF8

Write-Host "AOA WinUSB validation package prepared: $OutputDirectory"
