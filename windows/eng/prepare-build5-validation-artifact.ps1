[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [string]$ExtractDirectory =
        (Join-Path $env:USERPROFILE "Downloads\natsx-aoa-bootstrap-driver-x64-build5"),

    [switch]$ForceReextract
)

$ErrorActionPreference = "Stop"

$expectedZipSha256 =
    "C178B83A8EAA6EF4A839D9A518DA9ADD5E9779B610CBD2A1D6AFF364860C04FE"

$expectedCertificateThumbprint =
    "2F8BB1ACBDC192E4AF874EEA915CB7CE64C7D1A1"

$expectedCertificateSubject =
    "CN=NATSX AOA Bootstrap CI Test"

$expectedDriverVersion =
    "10/01/2026,0.1.0.2"

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Build-5 artifact ZIP does not exist: $ZipPath"
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
        throw "Required build-5 package file is missing after extraction: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Unexpected signing mode in build-5 manifest."
}

if ($manifest.TargetHardwareId -ne "USB\VID_22D9&PID_2764&REV_0404") {
    throw "Unexpected target hardware ID in build-5 manifest."
}

if ($manifest.InfVerif -ne "passed") {
    throw "Build-5 manifest does not record a passing InfVerif gate."
}

$manifestThumbprint =
    ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

if ($manifestThumbprint -ne $expectedCertificateThumbprint) {
    throw "Build-5 certificate thumbprint does not match the green CI artifact."
}

if ($manifest.TestCertificateSubject -ne $expectedCertificateSubject) {
    throw "Build-5 certificate subject mismatch."
}

$infText = Get-Content -LiteralPath $infPath -Raw

if ($infText -notmatch [regex]::Escape("DriverVer   = $expectedDriverVersion")) {
    throw "Build-5 INF does not contain expected DriverVer $expectedDriverVersion."
}

$driverHash = (Get-FileHash -LiteralPath $sysPath -Algorithm SHA256).Hash
$catalogHash = (Get-FileHash -LiteralPath $catPath -Algorithm SHA256).Hash
$certificateHash = (Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash

if ($driverHash -ne $manifest.DriverSha256) {
    throw "Build-5 SYS hash does not match package-manifest.json."
}

if ($catalogHash -ne $manifest.CatalogSha256) {
    throw "Build-5 CAT hash does not match package-manifest.json."
}

if ($certificateHash -ne $manifest.TestCertificateSha256) {
    throw "Build-5 CER hash does not match package-manifest.json."
}

$certificate =
    New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)

try {
    if ($certificate.Thumbprint.Replace(" ", "").ToUpperInvariant() -ne
        $expectedCertificateThumbprint) {
        throw "Extracted build-5 certificate identity mismatch."
    }

    if ($certificate.Subject -ne $expectedCertificateSubject) {
        throw "Extracted build-5 certificate subject mismatch."
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
    throw "Build-5 SYS signer does not match the expected CI certificate."
}

if ($catalogSigner -ne $expectedCertificateThumbprint) {
    throw "Build-5 CAT signer does not match the expected CI certificate."
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
