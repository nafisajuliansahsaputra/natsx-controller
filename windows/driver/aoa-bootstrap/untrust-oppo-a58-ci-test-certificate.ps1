[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [switch]$Untrust
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
if (-not (Test-Path $manifestPath)) {
    throw "Package manifest is missing: $manifestPath"
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$thumbprint = ([string]$manifest.TestCertificateThumbprint).Replace(" ", "").ToUpperInvariant()

if ($manifest.SigningMode -ne "ephemeral-ci-test" -or
    $thumbprint -notmatch "^[A-F0-9]{40}$") {
    throw "Manifest does not contain a safe ephemeral CI test-certificate identity."
}

if ($manifest.TestCertificateSubject -ne "CN=NATSX AOA Bootstrap CI Test") {
    throw "Refusing to remove an unexpected certificate subject."
}

$rootPath = "Cert:\LocalMachine\Root\$thumbprint"
$publisherPath = "Cert:\LocalMachine\TrustedPublisher\$thumbprint"

$preflight = [ordered]@{
    CertificateSubject = $manifest.TestCertificateSubject
    CertificateThumbprint = $thumbprint
    RootTrusted = Test-Path $rootPath
    TrustedPublisher = Test-Path $publisherPath
    UntrustRequested = [bool]$Untrust
}

$preflight | ConvertTo-Json -Depth 3

if (-not $Untrust) {
    Write-Host ""
    Write-Host "PRE-FLIGHT ONLY: no certificate-store changes were made."
    exit 0
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Removing the NATSX test certificate requires elevated PowerShell."
}

foreach ($path in @($publisherPath, $rootPath)) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Force
    }
}

if ((Test-Path $rootPath) -or (Test-Path $publisherPath)) {
    throw "At least one NATSX test-certificate trust entry remains after cleanup."
}

Write-Host ""
Write-Host "NATSX ephemeral CI test certificate removed from LocalMachine trust stores."
