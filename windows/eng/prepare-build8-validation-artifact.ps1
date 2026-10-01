[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [string]$ExtractDirectory =
        (Join-Path $env:USERPROFILE "Downloads\natsx-aoa-bootstrap-driver-x64-build8"),

    [switch]$ForceReextract
)

$ErrorActionPreference = "Stop"

$expectedZipSha256 =
    "F17E199A628C095EDAB4C1FD4900026FB9FBA1983615A1A242A8685C978DC29F"

$expectedCertificateThumbprint =
    "3131EA375084208EB7E800D5C84C1E270466DDD2"

$expectedCertificateSubject =
    "CN=NATSX AOA Bootstrap CI Test"

$expectedDriverVersion =
    "10/01/2026,0.1.0.5"

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Build-8 artifact ZIP does not exist: $ZipPath"
}

$zipHash =
    (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToUpperInvariant()

if ($zipHash -ne $expectedZipSha256) {
    throw "Artifact ZIP SHA-256 mismatch. Refusing to extract. Expected $expectedZipSha256, got $zipHash."
}

if (Test-Path -LiteralPath $ExtractDirectory) {
    if (-not $ForceReextract) {
        throw "Extract directory already exists: $ExtractDirectory. Use -ForceReextract only if you intentionally want to replace it."
    }

    Remove-Item -LiteralPath $ExtractDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $ExtractDirectory -Force | Out-Null
Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractDirectory -Force

$packageDirectory =
    Join-Path $ExtractDirectory "artifacts\oppo-a58-test-package"

$manifestPath = Join-Path $packageDirectory "package-manifest.json"
$infPath = Join-Path $packageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$sysPath = Join-Path $packageDirectory "Natsx.AoaBootstrap.Driver.sys"
$catPath = Join-Path $packageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.cat"
$cerPath = Join-Path $packageDirectory "Natsx.AoaBootstrap.CI-Test.cer"

foreach ($path in @($manifestPath, $infPath, $sysPath, $catPath, $cerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required build-6 package file is missing after extraction: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Unexpected signing mode in build-6 manifest."
}

if ($manifest.TargetHardwareId -ne "USB\VID_22D9&PID_2764&REV_0404") {
    throw "Unexpected target hardware ID in build-6 manifest."
}

if ($manifest.InfVerif -ne "passed") {
    throw "Build-8 manifest does not record a passing InfVerif gate."
}

$manifestThumbprint =
    ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

if ($manifestThumbprint -ne $expectedCertificateThumbprint) {
    throw "Build-8 certificate thumbprint does not match the green CI artifact."
}

if ($manifest.TestCertificateSubject -ne $expectedCertificateSubject) {
    throw "Build-8 certificate subject mismatch."
}

$infText = Get-Content -LiteralPath $infPath -Raw

if ($infText -notmatch [regex]::Escape("DriverVer   = $expectedDriverVersion")) {
    throw "Build-8 INF does not contain expected DriverVer $expectedDriverVersion."
}

$driverHash = (Get-FileHash -LiteralPath $sysPath -Algorithm SHA256).Hash
$catalogHash = (Get-FileHash -LiteralPath $catPath -Algorithm SHA256).Hash
$certificateHash = (Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash

if ($driverHash -ne $manifest.DriverSha256) {
    throw "Build-8 SYS hash does not match package-manifest.json."
}

if ($catalogHash -ne $manifest.CatalogSha256) {
    throw "Build-8 CAT hash does not match package-manifest.json."
}

if ($certificateHash -ne $manifest.TestCertificateSha256) {
    throw "Build-8 CER hash does not match package-manifest.json."
}

$certificate =
    New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)

try {
    if ($certificate.Thumbprint.Replace(" ", "").ToUpperInvariant() -ne
        $expectedCertificateThumbprint) {
        throw "Extracted build-6 certificate identity mismatch."
    }

    if ($certificate.Subject -ne $expectedCertificateSubject) {
        throw "Extracted build-6 certificate subject mismatch."
    }
}
finally {
    $certificate.Dispose()
}

$driverSignature = Get-AuthenticodeSignature -LiteralPath $sysPath
$catalogSignature = Get-AuthenticodeSignature -LiteralPath $catPath

$driverSigner =
    if ($null -ne $driverSignature.SignerCertificate) {
        $driverSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

$catalogSigner =
    if ($null -ne $catalogSignature.SignerCertificate) {
        $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

if ($driverSigner -ne $expectedCertificateThumbprint) {
    throw "Build-8 SYS signer does not match the expected CI certificate."
}

if ($catalogSigner -ne $expectedCertificateThumbprint) {
    throw "Build-8 CAT signer does not match the expected CI certificate."
}

[ordered]@{
    Prepared = $true
    ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
    ZipSha256 = $zipHash
    ExtractDirectory = (Resolve-Path -LiteralPath $ExtractDirectory).Path
    PackageDirectory = (Resolve-Path -LiteralPath $packageDirectory).Path
    TargetHardwareId = $manifest.TargetHardwareId
    InfVerif = $manifest.InfVerif
    SigningMode = $manifest.SigningMode
    DriverVersion = $expectedDriverVersion
    CertificateSubject = $manifest.TestCertificateSubject
    CertificateThumbprint = $manifestThumbprint
    DriverSignatureStatus = $driverSignature.Status.ToString()
    DriverSignerMatchesArtifact = ($driverSigner -eq $expectedCertificateThumbprint)
    CatalogSignatureStatus = $catalogSignature.Status.ToString()
    CatalogSignerMatchesArtifact = ($catalogSigner -eq $expectedCertificateThumbprint)
    SystemChangesMade = $false
} | ConvertTo-Json -Depth 4
