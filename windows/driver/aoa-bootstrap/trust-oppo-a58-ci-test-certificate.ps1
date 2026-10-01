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

foreach ($path in @($manifestPath, $cerPath)) {
    if (-not (Test-Path $path)) {
        throw "Required test-signing file is missing: $path"
    }
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test") {
    throw "Package is not marked as an ephemeral CI test-signed artifact."
}

$actualCerHash = (Get-FileHash -Path $cerPath -Algorithm SHA256).Hash
if ($actualCerHash -ne $manifest.TestCertificateSha256) {
    throw "Public test certificate hash does not match package-manifest.json."
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

    $preflight = [ordered]@{
        PackageDirectory = $PackageDirectory
        CertificateSubject = $certificate.Subject
        CertificateThumbprint = $certificate.Thumbprint
        CertificateNotAfterUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
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

    Write-Host ""
    Write-Host "NATSX ephemeral CI test certificate trusted for this physical-validation artifact."
    Write-Host "This does not enable TESTSIGNING or change Secure Boot."
}
finally {
    $certificate.Dispose()
}
