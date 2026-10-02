[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BootstrapPackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$WinUsbPackageDirectory
)

$ErrorActionPreference = "Stop"

$HardwareDriverVerificationOid = "1.3.6.1.4.1.311.10.3.5"
$AttestationVerificationOid = "1.3.6.1.4.1.311.10.3.5.1"

function Resolve-PackageDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Production driver package directory does not exist: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Find-SignTool {
    $programFilesX86 =
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFilesX86)

    $roots = @(
        (Join-Path $PSScriptRoot "aoa-bootstrap\packages"),
        (Join-Path $programFilesX86 "Windows Kits\10\bin")
    ) | Where-Object { Test-Path $_ }

    $matches = @(
        Get-ChildItem -Path $roots -Filter "signtool.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '(?i)[\\/]x64(?:[\\/]|$)' } |
            Sort-Object FullName -Descending
    )

    if ($matches.Count -eq 0) {
        throw "signtool.exe was not found. Install/restore the Windows Driver Kit before validating production driver packages."
    }

    return $matches[0].FullName
}

function Assert-File {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $path = Join-Path $Directory $Name

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required production driver file is missing: $path"
    }

    return $path
}

function Assert-NoDevelopmentArtifacts {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $forbidden = @(
        Get-ChildItem -LiteralPath $Directory -File -Recurse |
            Where-Object {
                $_.Name -match '(?i)CI[-_. ]?Test' -or
                $_.Extension -match '(?i)^\.(cer|pfx|p12|pvk)$'
            }
    )

    if ($forbidden.Count -gt 0) {
        $paths = $forbidden.FullName -join [Environment]::NewLine
        throw "Production bundle contains development/test certificate material or CI-test artifacts:$([Environment]::NewLine)$paths"
    }

    $manifest = Join-Path $Directory "package-manifest.json"

    if (Test-Path -LiteralPath $manifest -PathType Leaf) {
        $value = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json

        if ($value.PSObject.Properties.Name -contains "ShippingPackage" -and
            $value.ShippingPackage -eq $false) {
            throw "Package manifest explicitly marks this driver as non-shipping: $manifest"
        }

        if ($value.PSObject.Properties.Name -contains "SigningMode" -and
            [string]$value.SigningMode -match '(?i)(test|development|ephemeral|attestation)') {
            throw "Package manifest signing mode is not accepted for retail production: $($value.SigningMode)"
        }
    }
}

function Get-EnhancedKeyUsageOids {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $extension = @(
        $Certificate.Extensions |
            Where-Object { $_.Oid.Value -eq "2.5.29.37" }
    ) | Select-Object -First 1

    if ($null -eq $extension) {
        return @()
    }

    $eku =
        [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
            $extension,
            $extension.Critical)

    return @(
        $eku.EnhancedKeyUsages |
            ForEach-Object { $_.Value }
    )
}

function Assert-RetailMicrosoftCatalogSignature {
    param(
        [Parameter(Mandatory = $true)][string]$CatalogPath,
        [Parameter(Mandatory = $true)][string]$SignTool
    )

    & $SignTool verify /kp /v $CatalogPath

    if ($LASTEXITCODE -ne 0) {
        throw "Kernel-policy signature verification failed for catalog: $CatalogPath"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $CatalogPath

    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate) {
        throw "Catalog Authenticode signature is not valid: $CatalogPath ($($signature.Status))"
    }

    $oids =
        Get-EnhancedKeyUsageOids -Certificate $signature.SignerCertificate

    if ($oids -contains $AttestationVerificationOid) {
        throw "Attestation-signed catalog is testing-only and is rejected for NATSX retail production: $CatalogPath"
    }

    if ($oids -notcontains $HardwareDriverVerificationOid) {
        throw "Catalog does not carry the Microsoft Windows Hardware Driver Verification EKU required by the production gate: $CatalogPath"
    }
}

function Assert-CatalogContainsKernelBinary {
    param(
        [Parameter(Mandatory = $true)][string]$CatalogPath,
        [Parameter(Mandatory = $true)][string]$BinaryPath,
        [Parameter(Mandatory = $true)][string]$SignTool
    )

    & $SignTool verify /kp /v /c $CatalogPath $BinaryPath

    if ($LASTEXITCODE -ne 0) {
        throw "Production bootstrap catalog does not validate the KMDF driver binary under kernel policy: $BinaryPath"
    }
}

$bootstrap = Resolve-PackageDirectory -Path $BootstrapPackageDirectory
$winUsb = Resolve-PackageDirectory -Path $WinUsbPackageDirectory

Assert-NoDevelopmentArtifacts -Directory $bootstrap
Assert-NoDevelopmentArtifacts -Directory $winUsb

$bootstrapInf = Assert-File -Directory $bootstrap -Name "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$bootstrapSys = Assert-File -Directory $bootstrap -Name "Natsx.AoaBootstrap.Driver.sys"
$bootstrapCat = Assert-File -Directory $bootstrap -Name "Natsx.AoaBootstrap.OPPOA58.Extension.cat"
$winUsbInf = Assert-File -Directory $winUsb -Name "NatsxAoaWinUsb.inf"
$winUsbCat = Assert-File -Directory $winUsb -Name "NatsxAoaWinUsb.cat"

$bootstrapInfText = Get-Content -LiteralPath $bootstrapInf -Raw
$requiredBootstrapId = "USB\VID_22D9&PID_2764&REV_0404"

if ($bootstrapInfText -notmatch [regex]::Escape($requiredBootstrapId)) {
    throw "Production bootstrap INF is missing the exact validated OPPO A58 hardware ID."
}

if ($bootstrapInfText -match 'USB\\VID_22D9&PID_2764\s*(?:$|[,;])' -or
    $bootstrapInfText -match 'USB\\(?:Class_|MS_COMP_)' -or
    $bootstrapInfText -match 'PID_2765') {
    throw "Production bootstrap INF contains a broader or unsupported hardware match."
}

$winUsbInfText = Get-Content -LiteralPath $winUsbInf -Raw

foreach ($id in @("USB\VID_18D1&PID_2D00", "USB\VID_18D1&PID_2D01&MI_00")) {
    if ($winUsbInfText -notmatch [regex]::Escape($id)) {
        throw "Production WinUSB INF is missing required exact AOA target '$id'."
    }
}

if ($winUsbInfText -match '(?im)^.*USB\\VID_18D1&PID_2D01\s*$' -or
    $winUsbInfText -match '(?im)USB\\Class_|DefaultInstall|ClassInstall32') {
    throw "Production WinUSB INF contains a broader or legacy install match."
}

$signTool = Find-SignTool

Assert-RetailMicrosoftCatalogSignature -CatalogPath $bootstrapCat -SignTool $signTool
Assert-CatalogContainsKernelBinary -CatalogPath $bootstrapCat -BinaryPath $bootstrapSys -SignTool $signTool
Assert-RetailMicrosoftCatalogSignature -CatalogPath $winUsbCat -SignTool $signTool

Write-Host "Production USB driver package validation passed."
Write-Host "  Bootstrap: $bootstrap"
Write-Host "  WinUSB:    $winUsb"
