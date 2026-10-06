[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [switch]$Trust
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $scriptRoot "artifacts\test-package"
}

$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$cerPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.CI-Test.cer"
$catPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.cat"

foreach ($path in @($manifestPath, $cerPath, $catPath)) {
    if (-not (Test-Path $path)) { throw "Required AOA WinUSB signing file missing: $path" }
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ($manifest.SigningMode -ne "ephemeral-ci-test" -or
    $manifest.TestCertificateSubject -ne "CN=NATSX AOA WinUSB CI Test") {
    throw "Unexpected AOA WinUSB signing identity."
}

if ((Get-FileHash $cerPath -Algorithm SHA256).Hash -ne $manifest.TestCertificateSha256) {
    throw "AOA WinUSB certificate hash mismatch."
}

if ((Get-FileHash $catPath -Algorithm SHA256).Hash -ne $manifest.CatalogSha256) {
    throw "AOA WinUSB catalog hash mismatch."
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
try {
    if ($certificate.Thumbprint -ne $manifest.TestCertificateThumbprint) {
        throw "AOA WinUSB certificate thumbprint mismatch."
    }

    $catalogSignature = Get-AuthenticodeSignature $catPath
    $catalogSigner = if ($null -ne $catalogSignature.SignerCertificate) {
        $catalogSignature.SignerCertificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    } else { $null }

    $expected = $certificate.Thumbprint.Replace(" ", "").ToUpperInvariant()
    if ($catalogSigner -ne $expected) {
        throw "AOA WinUSB catalog signer mismatch."
    }

    $report = [ordered]@{
        CertificateSubject = $certificate.Subject
        CertificateThumbprint = $certificate.Thumbprint
        CatalogSignatureStatus = $catalogSignature.Status.ToString()
        CatalogSignerMatchesArtifactCertificate = $true
        RootTrusted = Test-Path ("Cert:\LocalMachine\Root\" + $certificate.Thumbprint)
        TrustedPublisher = Test-Path ("Cert:\LocalMachine\TrustedPublisher\" + $certificate.Thumbprint)
        TrustRequested = [bool]$Trust
    }
    $report | ConvertTo-Json -Depth 3

    if (-not $Trust) {
        Write-Host "PRE-FLIGHT ONLY: no trust-store changes were made."
        exit 0
    }

    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Trusting the AOA WinUSB test certificate requires elevated PowerShell."
    }

    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null

    if ((Get-AuthenticodeSignature $catPath).Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "AOA WinUSB catalog signature is not Valid after trust."
    }

    Write-Host "NATSX AOA WinUSB CI test certificate trusted."
}
finally {
    $certificate.Dispose()
}
