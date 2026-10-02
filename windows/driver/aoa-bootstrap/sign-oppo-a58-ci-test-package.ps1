[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$CertificateSubject = "CN=NATSX AOA Bootstrap CI Test"
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

$infPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$sysPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.Driver.sys"
$catPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.OPPOA58.Extension.cat"
$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$cerPath = Join-Path $PackageDirectory "Natsx.AoaBootstrap.CI-Test.cer"

foreach ($path in @($infPath, $sysPath, $manifestPath)) {
    if (-not (Test-Path $path)) {
        throw "Required package file is missing: $path"
    }
}

function Find-WdkTool {
    param([Parameter(Mandatory = $true)][string]$Name)

    $roots = @(
        (Join-Path $scriptRoot "packages"),
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin")
    ) | Where-Object { Test-Path $_ }

    $matches = @(
        Get-ChildItem -Path $roots -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object @{
                Expression = {
                    if ($_.DirectoryName -match "(?i)[\\/]x64(?:[\\/]|$)") { 0 }
                    elseif ($_.DirectoryName -match "(?i)[\\/]x86(?:[\\/]|$)") { 1 }
                    else { 2 }
                }
            }, FullName
    )

    if ($matches.Count -eq 0) { return $null }

    # Some WDK tools (including Inf2Cat in certain NuGet layouts) are shipped
    # only under x86 even when they validate x64 driver packages.
    return $matches[0].FullName
}

$inf2Cat = Find-WdkTool -Name "inf2cat.exe"
$signTool = Find-WdkTool -Name "signtool.exe"

if ([string]::IsNullOrWhiteSpace($inf2Cat)) {
    throw "inf2cat.exe was not found in the restored WDK packages or installed Windows Kits."
}

if ([string]::IsNullOrWhiteSpace($signTool)) {
    throw "signtool.exe was not found in the restored WDK packages or installed Windows Kits."
}

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $CertificateSubject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddDays(14)

try {
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

    # HVCI requires the kernel image itself to carry a digital signature, so
    # sign the SYS before generating the catalog that hashes package files.
    & $signTool sign /v /fd SHA256 /sha1 $cert.Thumbprint /s My $sysPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed while embedding the SYS test signature."
    }

    if (Test-Path $catPath) {
        Remove-Item -Path $catPath -Force
    }

    $osTargets = "10_VB_X64,10_CO_X64,10_NI_X64,10_GE_X64,10_25H2_X64"
    & $inf2Cat /v "/driver:$PackageDirectory" "/os:$osTargets"
    if ($LASTEXITCODE -ne 0) {
        throw "Inf2Cat failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $catPath)) {
        throw "Inf2Cat completed without producing the expected catalog."
    }

    & $signTool sign /v /fd SHA256 /sha1 $cert.Thumbprint /s My $catPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed while signing the driver catalog."
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName SigningMode -NotePropertyValue "ephemeral-ci-test" -Force
    $manifest | Add-Member -NotePropertyName TestCertificateSubject -NotePropertyValue $cert.Subject -Force
    $manifest | Add-Member -NotePropertyName TestCertificateThumbprint -NotePropertyValue $cert.Thumbprint -Force
    $manifest | Add-Member -NotePropertyName TestCertificateNotAfterUtc -NotePropertyValue $cert.NotAfter.ToUniversalTime().ToString("O") -Force
    $manifest | Add-Member -NotePropertyName DriverSha256 -NotePropertyValue ((Get-FileHash -Path $sysPath -Algorithm SHA256).Hash) -Force
    $manifest | Add-Member -NotePropertyName CatalogSha256 -NotePropertyValue ((Get-FileHash -Path $catPath -Algorithm SHA256).Hash) -Force
    $manifest | Add-Member -NotePropertyName TestCertificateSha256 -NotePropertyValue ((Get-FileHash -Path $cerPath -Algorithm SHA256).Hash) -Force
    $manifest | Add-Member -NotePropertyName CatalogCreatedFor -NotePropertyValue $osTargets -Force
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-Host ""
    Write-Host "Ephemeral CI test-signed package prepared."
    Write-Host "Certificate thumbprint: $($cert.Thumbprint)"
    Write-Host "Private key remains only in the current runner/user certificate store and is not exported."
}
finally {
    # Remove the ephemeral private key from the signing machine after package
    # generation. The artifact contains only the public certificate.
    Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue
}
