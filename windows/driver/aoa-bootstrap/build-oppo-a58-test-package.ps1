[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDirectory,
    [switch]$SkipInfVerif
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptRoot "artifacts\oppo-a58-test-package"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$driverBinary = Join-Path $scriptRoot "$Platform\$Configuration\Natsx.AoaBootstrap.Driver.sys"
$inxPath = Join-Path $scriptRoot "Natsx.AoaBootstrap.OPPOA58.Extension.inx"
$infName = "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$infPath = Join-Path $OutputDirectory $infName
$packageDriver = Join-Path $OutputDirectory "Natsx.AoaBootstrap.Driver.sys"

if (-not (Test-Path $driverBinary)) {
    throw "Driver binary not found at '$driverBinary'. Build the KMDF driver first."
}

if (-not (Test-Path $inxPath)) {
    throw "INF template not found at '$inxPath'."
}

$inx = Get-Content -Path $inxPath -Raw

$requiredExactTarget = "USB\VID_22D9&PID_2764&REV_0404"
if ($inx -notmatch [regex]::Escape($requiredExactTarget)) {
    throw "The OPPO A58 package no longer targets the exact validated no-ADB hardware ID '$requiredExactTarget'."
}

$modelsSectionMatch = [regex]::Match(
    $inx,
    '(?ms)^\[NatsxModels\.NTamd64\.10\.0\.\.\.22000\]\s*(?<body>.*?)(?=^\[|\z)')

if (-not $modelsSectionMatch.Success) {
    throw "Could not find the OPPO A58 Models section in the INF template."
}

$modelsBody = $modelsSectionMatch.Groups["body"].Value

if ($modelsBody -notmatch [regex]::Escape($requiredExactTarget)) {
    throw "The Models section does not contain the exact validated OPPO A58 hardware ID."
}

if ($modelsBody -match 'USB\\VID_22D9&PID_2764\s*(?:$|[,;])') {
    throw "The Models section contains a broader PID-only match. Refusing to package."
}

if ($modelsBody -match 'USB\\(?:Class_|MS_COMP_)') {
    throw "The Models section contains a class/compatible-ID match. Refusing to package."
}

if ($modelsBody -match 'PID_2765') {
    throw "The Models section still targets the ADB-on PID_2765. Refusing to package."
}

if ($inx -notmatch '(?ms)^\[NatsxAoaBootstrap_Install\.NT\.Filters\]\s*AddFilter\s*=\s*NatsxAoaBootstrap\s*,\s*0\s*,') {
    throw "The package does not register NatsxAoaBootstrap through DDInstall.Filters."
}

if ($inx -notmatch '(?ms)^\[NatsxAoaBootstrap_Filter\]\s*FilterPosition\s*=\s*Lower\s*$') {
    throw "The package is not explicitly scoped as a device lower filter."
}

if ($inx -match '(?im)ClassInstall32|DefaultInstall') {
    throw "The package contains an unexpected class-wide or legacy install directive."
}

if (Test-Path $OutputDirectory) {
    Remove-Item -Path $OutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -Path $driverBinary -Destination $packageDriver -Force

$inf = $inx.Replace('$KMDFVERSION$', '1.15')

if ($inf -match '\$[A-Z][A-Z0-9_]*\$') {
    throw "The materialized INF still contains unresolved WDK template tokens."
}

Set-Content -Path $infPath -Value $inf -Encoding Unicode

function Find-WdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $candidateRoots = @(
        (Join-Path $scriptRoot "packages"),
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin")
    ) | Where-Object { Test-Path $_ }

    $matches = @(
        Get-ChildItem -Path $candidateRoots -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '(?i)[\\/]x64(?:[\\/]|$)' } |
            Sort-Object FullName -Descending
    )

    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[0].FullName
}

$infVerifPath = $null
if (-not $SkipInfVerif) {
    $infVerifPath = Find-WdkTool -Name "infverif.exe"

    if ([string]::IsNullOrWhiteSpace($infVerifPath)) {
        throw "infverif.exe was not found in the restored WDK packages or installed Windows Kits."
    }

    Write-Host "Validating INF with: $infVerifPath"
    & $infVerifPath /w /v $infPath

    if ($LASTEXITCODE -ne 0) {
        throw "InfVerif failed with exit code $LASTEXITCODE."
    }
}

$manifest = [ordered]@{
    PackageKind = "NATSX AOA bootstrap physical-validation package"
    ShippingPackage = $false
    TargetDevice = "OPPO A58 / CPH2577"
    TargetHardwareId = $requiredExactTarget
    ExpectedClass = "WPD"
    ExpectedService = "WUDFWpdMtp"
    ExpectedBaseInf = "wpdmtp.inf"
    ADBRequired = $false
    UsbTetheringRequired = $false
    FilterService = "NatsxAoaBootstrap"
    FilterPosition = "Lower"
    DriverSha256 = (Get-FileHash -Path $packageDriver -Algorithm SHA256).Hash
    InfSha256 = (Get-FileHash -Path $infPath -Algorithm SHA256).Hash
    InfVerif = if ($SkipInfVerif) { "skipped" } else { "passed" }
}

$manifestPath = Join-Path $OutputDirectory "package-manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "NATSX OPPO A58 test package prepared:"
Write-Host "  $OutputDirectory"
Write-Host ""
Write-Host "This package is for physical validation only."
Write-Host "It is not signed and must not be installed yet."
