[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDirectory = "artifacts/windows-driver-hlk-payload"
)

$ErrorActionPreference = "Stop"

$driverRoot = $PSScriptRoot
$repoRoot =
    (Resolve-Path (Join-Path $driverRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version =
        (Get-Content (Join-Path $repoRoot "VERSION") -Raw)
            .Trim() -replace '-dev$', ''
}

if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$') {
    throw "Driver payload version must be stable x.y.z, got '$Version'."
}

$driverVersion =
    "$($Matches.major).$($Matches.minor).$($Matches.patch).0"

$commitDateText =
    (& git -C $repoRoot show -s --format=%cs HEAD 2>$null |
        Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($commitDateText)) {
    throw "Could not resolve the current Git commit date for deterministic DriverVer metadata."
}

$commitDate =
    [DateTime]::ParseExact(
        $commitDateText.Trim(),
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture)

$driverDate =
    $commitDate.ToString(
        "MM/dd/yyyy",
        [Globalization.CultureInfo]::InvariantCulture)

$output =
    if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    }
    else {
        Join-Path $repoRoot $OutputDirectory
    }

$bootstrapSource =
    Join-Path $driverRoot "aoa-bootstrap"
$winUsbSource =
    Join-Path $driverRoot "aoa-winusb"
$bootstrapOutput =
    Join-Path $output "aoa-bootstrap"
$winUsbOutput =
    Join-Path $output "aoa-winusb"

$driverBinary =
    Join-Path $bootstrapSource "$Platform\$Configuration\Natsx.AoaBootstrap.Driver.sys"
$bootstrapInx =
    Join-Path $bootstrapSource "Natsx.AoaBootstrap.OPPOA58.Extension.inx"
$winUsbInfSource =
    Join-Path $winUsbSource "NatsxAoaWinUsb.inf"

foreach ($required in @(
    $driverBinary,
    $bootstrapInx,
    $winUsbInfSource
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required HLK payload input is missing: $required"
    }
}

function Find-WdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $programFilesX86 =
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFilesX86)

    $roots = @(
        (Join-Path $bootstrapSource "packages"),
        (Join-Path $programFilesX86 "Windows Kits\10\bin")
    ) | Where-Object {
        Test-Path $_
    }

    $matches = @(
        Get-ChildItem -Path $roots -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object @{
                Expression = {
                    if ($_.DirectoryName -match '(?i)[\\/]x64(?:[\\/]|$)') {
                        0
                    }
                    elseif ($_.DirectoryName -match '(?i)[\\/]x86(?:[\\/]|$)') {
                        1
                    }
                    else {
                        2
                    }
                }
            }, FullName
    )

    if ($matches.Count -eq 0) {
        throw "$Name was not found in restored WDK packages or Windows Kits."
    }

    return $matches[0].FullName
}

function Assert-ExactBootstrapScope {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $requiredId =
        "USB\VID_22D9&PID_2764&REV_0404"

    if ($Text -notmatch [regex]::Escape($requiredId)) {
        throw "Bootstrap INF template is missing the exact validated OPPO A58 hardware ID."
    }

    $models =
        [regex]::Match(
            $Text,
            '(?ms)^\[NatsxModels\.NTamd64\.10\.0\.\.\.22000\]\s*(?<body>.*?)(?=^\[|\z)')

    if (-not $models.Success) {
        throw "Bootstrap INF template is missing the expected Models section."
    }

    $body =
        $models.Groups["body"].Value

    if ($body -match 'USB\\VID_22D9&PID_2764\s*(?:$|[,;])' -or
        $body -match 'USB\\(?:Class_|MS_COMP_)' -or
        $body -match 'PID_2765') {
        throw "Bootstrap INF template contains a broader or unsupported hardware match."
    }

    if ($Text -notmatch '(?ms)^\[NatsxAoaBootstrap_Install\.NT\.Filters\]\s*AddFilter\s*=\s*NatsxAoaBootstrap\s*,\s*0\s*,') {
        throw "Bootstrap INF no longer registers through DDInstall.Filters."
    }

    if ($Text -notmatch '(?ms)^\[NatsxAoaBootstrap_Filter\]\s*FilterPosition\s*=\s*Lower\s*$') {
        throw "Bootstrap INF is no longer a device-scoped lower filter."
    }

    if ($Text -match '(?im)ClassInstall32|DefaultInstall') {
        throw "Bootstrap INF contains an unexpected class-wide or legacy install directive."
    }
}

function Assert-ExactWinUsbScope {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    foreach ($id in @(
        "USB\VID_18D1&PID_2D00",
        "USB\VID_18D1&PID_2D01&MI_00"
    )) {
        if ($Text -notmatch [regex]::Escape($id)) {
            throw "AOA WinUSB INF is missing exact target '$id'."
        }
    }

    if ($Text -match '(?im)^.*USB\\VID_18D1&PID_2D01\s*$' -or
        $Text -match '(?im)USB\\Class_|DefaultInstall|ClassInstall32') {
        throw "AOA WinUSB INF contains a broader or legacy install match."
    }

    if ($Text -notmatch [regex]::Escape(
            "{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}")) {
        throw "AOA WinUSB INF is missing the NATSX interface GUID."
    }

    if ($Text -notmatch '(?im)^Include\s*=\s*winusb\.inf\s*$' -or
        $Text -notmatch '(?im)^Needs\s*=\s*WINUSB\.NT\s*$') {
        throw "AOA WinUSB INF no longer delegates to the inbox WinUSB driver."
    }
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Path $bootstrapOutput -Force |
    Out-Null
New-Item -ItemType Directory -Path $winUsbOutput -Force |
    Out-Null

$bootstrapText =
    Get-Content -LiteralPath $bootstrapInx -Raw

Assert-ExactBootstrapScope -Text $bootstrapText

$bootstrapText =
    $bootstrapText.Replace(
        '$KMDFVERSION$',
        '1.15')

$bootstrapText =
    [regex]::Replace(
        $bootstrapText,
        '(?im)^DriverVer\s*=\s*[^\r\n]+$',
        "DriverVer   = $driverDate,$driverVersion")

$bootstrapText =
    $bootstrapText.Replace(
        "NATSX AOA bootstrap prototype package",
        "NATSX AOA bootstrap package")

if ($bootstrapText -match '\$[A-Z][A-Z0-9_]*\$') {
    throw "Materialized bootstrap INF still contains an unresolved template token."
}

$bootstrapInf =
    Join-Path $bootstrapOutput "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$bootstrapSys =
    Join-Path $bootstrapOutput "Natsx.AoaBootstrap.Driver.sys"

Set-Content -LiteralPath $bootstrapInf -Value $bootstrapText -Encoding Unicode
Copy-Item -LiteralPath $driverBinary -Destination $bootstrapSys -Force

$winUsbText =
    Get-Content -LiteralPath $winUsbInfSource -Raw

Assert-ExactWinUsbScope -Text $winUsbText

$winUsbText =
    [regex]::Replace(
        $winUsbText,
        '(?im)^DriverVer\s*=\s*[^\r\n]+$',
        "DriverVer=$driverDate,$driverVersion")

$winUsbInf =
    Join-Path $winUsbOutput "NatsxAoaWinUsb.inf"

Set-Content -LiteralPath $winUsbInf -Value $winUsbText -Encoding Unicode

$infVerif =
    Find-WdkTool -Name "infverif.exe"
$inf2Cat =
    Find-WdkTool -Name "inf2cat.exe"

foreach ($inf in @(
    $bootstrapInf,
    $winUsbInf
)) {
    Write-Host "InfVerif: $inf"
    & $infVerif /w /v $inf

    if ($LASTEXITCODE -ne 0) {
        throw "InfVerif failed for '$inf' with exit code $LASTEXITCODE."
    }
}

$osTargets =
    "10_VB_X64,10_CO_X64,10_NI_X64,10_GE_X64,10_25H2_X64"

foreach ($packageDirectory in @(
    $bootstrapOutput,
    $winUsbOutput
)) {
    Write-Host "Inf2Cat: $packageDirectory"
    & $inf2Cat /v "/driver:$packageDirectory" "/os:$osTargets"

    if ($LASTEXITCODE -ne 0) {
        throw "Inf2Cat failed for '$packageDirectory' with exit code $LASTEXITCODE."
    }
}

$bootstrapCat =
    Join-Path $bootstrapOutput "Natsx.AoaBootstrap.OPPOA58.Extension.cat"
$winUsbCat =
    Join-Path $winUsbOutput "NatsxAoaWinUsb.cat"

foreach ($catalog in @(
    $bootstrapCat,
    $winUsbCat
)) {
    if (-not (Test-Path -LiteralPath $catalog -PathType Leaf)) {
        throw "Inf2Cat did not create expected catalog: $catalog"
    }

    $signature =
        Get-AuthenticodeSignature -LiteralPath $catalog

    if ($signature.Status -eq
        [System.Management.Automation.SignatureStatus]::Valid) {
        throw "HLK payload catalog is already signed. Submission payload generation must not reuse a signed/test-signed catalog."
    }
}

$manifest =
    [ordered]@{
        PackageKind =
            "NATSX Windows Hardware Compatibility / HLK driver payload"
        ShippingPackage =
            $false
        SubmissionOnly =
            $true
        ProductVersion =
            $Version
        DriverVersion =
            $driverVersion
        DriverDate =
            $driverDate
        SourceCommit =
            (& git -C $repoRoot rev-parse HEAD).Trim()
        CatalogCreatedFor =
            $osTargets
        BootstrapHardwareId =
            "USB\VID_22D9&PID_2764&REV_0404"
        WinUsbHardwareIds =
            @(
                "USB\VID_18D1&PID_2D00",
                "USB\VID_18D1&PID_2D01&MI_00"
            )
        DeviceInterfaceGuid =
            "{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}"
        Files =
            [ordered]@{
                BootstrapInfSha256 =
                    (Get-FileHash -LiteralPath $bootstrapInf -Algorithm SHA256).Hash
                BootstrapSysSha256 =
                    (Get-FileHash -LiteralPath $bootstrapSys -Algorithm SHA256).Hash
                BootstrapUnsignedCatalogSha256 =
                    (Get-FileHash -LiteralPath $bootstrapCat -Algorithm SHA256).Hash
                WinUsbInfSha256 =
                    (Get-FileHash -LiteralPath $winUsbInf -Algorithm SHA256).Hash
                WinUsbUnsignedCatalogSha256 =
                    (Get-FileHash -LiteralPath $winUsbCat -Algorithm SHA256).Hash
            }
    }

$manifestPath =
    Join-Path $output "submission-manifest.json"

$manifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

$readmePath =
    Join-Path $output "README.txt"

@"
NATSX WINDOWS DRIVER HLK PAYLOAD
================================

This artifact is NOT a retail-shipping driver bundle.

Use the aoa-bootstrap and aoa-winusb payloads as inputs to the Windows HLK /
Windows Hardware Compatibility Program process. Complete the applicable HLK
tests and create the signed submission package required by Microsoft Partner
Center / Hardware Dev Center.

After Microsoft returns the production-signed packages, do NOT copy this
top-level submission-manifest.json into the shipping driver directories.
Stage only the Microsoft-returned INF/SYS/CAT package files, then run:

  windows\driver\validate-production-packages.ps1

The production validator intentionally rejects CI test signing, attestation-only
signing, missing kernel-policy signatures, broadened hardware IDs, and
development certificate material.
"@ |
    Set-Content -LiteralPath $readmePath -Encoding UTF8

Write-Host ""
Write-Host "HLK driver payload prepared:"
Write-Host "  $output"
Write-Host "Version: $driverVersion ($driverDate)"
Write-Host ""
Write-Host "This artifact is submission-only and is not production-shipping material."
