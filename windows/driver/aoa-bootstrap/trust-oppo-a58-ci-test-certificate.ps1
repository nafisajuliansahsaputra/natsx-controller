[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [switch]$Trust
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $scriptRoot "artifacts\oppo-a58-test-package"
}
elseif (-not [System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot $PackageDirectory
}

$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$cerPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.CI-Test.cer"
$sysPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.Driver.sys"
$catPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.cat"

foreach ($path in @($manifestPath, $cerPath, $sysPath, $catPath)) {
    if (-not (Test-Path $path)) {
        throw "Required test-signing file is missing: $path"
    }
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Package is not marked as an ephemeral CI test-signed artifact."
}

$actualCerHash = (Get-FileHash -Path $cerPath -Algorithm SHA256).Hash
$actualDriverHash = (Get-FileHash -Path $sysPath -Algorithm SHA256).Hash
$actualCatalogHash = (Get-FileHash -Path $catPath -Algorithm SHA256).Hash

if ($actualCerHash -ne $manifest.TestCertificateSha256) {
    throw "Public test certificate hash does not match package-manifest.json."
}

if ($actualDriverHash -ne $manifest.DriverSha256) {
    throw "Kernel driver hash does not match package-manifest.json."
}

if ($actualCatalogHash -ne $manifest.CatalogSha256) {
    throw "Catalog hash does not match package-manifest.json."
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
try {
    if ($certificate.Thumbprint -ne $manifest.TestCertificateThumbprint) {
        throw "Public test certificate thumbprint does not match package-manifest.json."
    }

    if ($certificate.Subject -ne $manifest.TestCertificateSubject) {
        throw "Public test certificate subject does not match package-manifest.json."
    }

    if ($certificate.Subject -ne "CN=NATSX AOA Bootstrap CI Test") {
        throw "Refusing to trust an unexpected certificate subject."
    }

    $driverSignature = Get-AuthenticodeSignature -FilePath $sysPath
    $catalogSignature = Get-AuthenticodeSignature -FilePath $catPath

    $driverSignerThumbprint =
        if ($null -ne $driverSignature.SignerCertificate) {
            $driverSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
        }
        else {
            $null
        }

    $catalogSignerThumbprint =
        if ($null -ne $catalogSignature.SignerCertificate) {
            $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
        }
        else {
            $null
        }

    $expectedThumbprint =
        $certificate.Thumbprint.Replace(" ", "").ToUpperInvariant()

    if ($driverSignerThumbprint -ne $expectedThumbprint) {
        throw "Refusing trust: kernel driver signer does not match the artifact's public test certificate."
    }

    if ($catalogSignerThumbprint -ne $expectedThumbprint) {
        throw "Refusing trust: catalog signer does not match the artifact's public test certificate."
    }

    $preflight = [ordered]@{
        PackageDirectory = $PackageDirectory
        CertificateSubject = $certificate.Subject
        CertificateThumbprint = $certificate.Thumbprint
        CertificateNotAfterUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
        DriverSignatureStatus = $driverSignature.Status.ToString()
        DriverSignatureStatusMessage = $driverSignature.StatusMessage
        DriverSignerThumbprint = $driverSignerThumbprint
        DriverSignerMatchesArtifactCertificate = $true
        CatalogSignatureStatus = $catalogSignature.Status.ToString()
        CatalogSignatureStatusMessage = $catalogSignature.StatusMessage
        CatalogSignerThumbprint = $catalogSignerThumbprint
        CatalogSignerMatchesArtifactCertificate = $true
        RootTrusted = Test-Path ("Cert:\LocalMachine\Root\" + $certificate.Thumbprint)
        TrustedPublisher = Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $certificate.Thumbprint)
        TrustRequested = [bool]$Trust
    }

    $preflight | ConvertTo-Json -Depth 3

    if (-not $Trust) {
        Write-Host ""
        Write-Host "PRE-FLIGHT ONLY: the CI test certificate was not trusted."
        exit 0
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Trusting the kernel test certificate requires elevated PowerShell."
    }

    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null

    if (-not (Test-Path ("Cert:\LocalMachine\Root\" + $certificate.Thumbprint)) -or
        -not (Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $certificate.Thumbprint))) {
        throw "Certificate import did not produce both required trust-store entries."
    }

    $trustedDriverSignature = Get-AuthenticodeSignature -FilePath $sysPath
    $trustedCatalogSignature = Get-AuthenticodeSignature -FilePath $catPath

    if ($trustedDriverSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Certificate was imported, but the kernel driver signature is still not Valid."
    }

    if ($trustedCatalogSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Certificate was imported, but the catalog signature is still not Valid."
    }

    Write-Host ""
    Write-Host "NATSX ephemeral CI test certificate trusted for this physical-validation artifact."
    Write-Host "This does not enable TESTSIGNING or change Secure Boot."
}
finally {
    $certificate.Dispose()
}
