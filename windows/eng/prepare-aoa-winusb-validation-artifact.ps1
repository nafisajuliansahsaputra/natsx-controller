[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [string]$ExtractDirectory =
        (Join-Path $env:USERPROFILE "Downloads\natsx-aoa-winusb-driver-x64"),

    [switch]$ForceReextract
)

$ErrorActionPreference = "Stop"

$expectedZipSha256 =
    "EC02A62D175E4F0D1490237D178E413A519A707CD4DA232B3AE6C42FAB99600A"

$expectedCertificateThumbprint =
    "52981C0212EB3481D5DF4A1482F25C725EA70883"

$expectedCertificateSubject =
    "CN=NATSX AOA WinUSB CI Test"

$requiredIds = @(
    "USB\VID_18D1&PID_2D00",
    "USB\VID_18D1&PID_2D01&MI_00"
)

$requiredGuid =
    "{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}"

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "AOA WinUSB artifact ZIP does not exist: $ZipPath"
}

$zipHash =
    (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToUpperInvariant()

if ($zipHash -ne $expectedZipSha256) {
    throw "AOA WinUSB ZIP SHA-256 mismatch. Expected $expectedZipSha256, got $zipHash."
}

if (Test-Path -LiteralPath $ExtractDirectory) {
    if (-not $ForceReextract) {
        throw "Extract directory already exists: $ExtractDirectory. Use -ForceReextract only to replace it."
    }

    Remove-Item -LiteralPath $ExtractDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $ExtractDirectory -Force | Out-Null
Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractDirectory -Force

$manifestPath = Join-Path $ExtractDirectory "package-manifest.json"
$infPath = Join-Path $ExtractDirectory "NatsxAoaWinUsb.inf"
$catPath = Join-Path $ExtractDirectory "NatsxAoaWinUsb.cat"
$cerPath = Join-Path $ExtractDirectory "NatsxAoaWinUsb.CI-Test.cer"

foreach ($path in @($manifestPath, $infPath, $catPath, $cerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required AOA WinUSB package file is missing: $path"
    }
}

$manifest =
    Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Unexpected AOA WinUSB signing mode."
}

if ($manifest.InfVerif -ne "passed" -or
    -not [bool]$manifest.UsesInboxWinUsb) {
    throw "AOA WinUSB package did not pass required package gates."
}

foreach ($id in $requiredIds) {
    if (@($manifest.SupportedHardwareIds) -notcontains $id) {
        throw "AOA WinUSB manifest is missing exact target '$id'."
    }
}

if ($manifest.DeviceInterfaceGuid -ne $requiredGuid) {
    throw "AOA WinUSB device interface GUID mismatch."
}

$thumbprint =
    ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

if ($thumbprint -ne $expectedCertificateThumbprint) {
    throw "AOA WinUSB certificate thumbprint does not match the green CI artifact."
}

if ($manifest.TestCertificateSubject -ne $expectedCertificateSubject) {
    throw "AOA WinUSB certificate subject mismatch."
}

if ((Get-FileHash -LiteralPath $catPath -Algorithm SHA256).Hash -ne
    $manifest.CatalogSha256) {
    throw "AOA WinUSB CAT hash does not match package-manifest.json."
}

if ((Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash -ne
    $manifest.TestCertificateSha256) {
    throw "AOA WinUSB CER hash does not match package-manifest.json."
}

$certificate =
    New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)

try {
    if ($certificate.Thumbprint.Replace(" ", "").ToUpperInvariant() -ne
        $expectedCertificateThumbprint) {
        throw "Extracted AOA WinUSB certificate identity mismatch."
    }

    if ($certificate.Subject -ne $expectedCertificateSubject) {
        throw "Extracted AOA WinUSB certificate subject mismatch."
    }
}
finally {
    $certificate.Dispose()
}

$catalogSignature =
    Get-AuthenticodeSignature -LiteralPath $catPath

$catalogSigner =
    if ($null -ne $catalogSignature.SignerCertificate) {
        $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    }
    else {
        $null
    }

if ($catalogSigner -ne $expectedCertificateThumbprint) {
    throw "AOA WinUSB catalog signer does not match the expected CI certificate."
}

[ordered]@{
    Prepared = $true
    ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
    ZipSha256 = $zipHash
    PackageDirectory = (Resolve-Path -LiteralPath $ExtractDirectory).Path
    SupportedHardwareIds = $requiredIds
    DeviceInterfaceGuid = $requiredGuid
    UsesInboxWinUsb = [bool]$manifest.UsesInboxWinUsb
    InfVerif = $manifest.InfVerif
    SigningMode = $manifest.SigningMode
    CertificateSubject = $manifest.TestCertificateSubject
    CertificateThumbprint = $thumbprint
    CatalogSignatureStatus = $catalogSignature.Status.ToString()
    CatalogSignerMatchesArtifact = ($catalogSigner -eq $expectedCertificateThumbprint)
    SystemChangesMade = $false
} | ConvertTo-Json -Depth 4
