[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$CertificateSubject = "CN=NATSX AOA WinUSB CI Test"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..\..")

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $scriptRoot "artifacts\test-package"
}
elseif (-not [System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot $PackageDirectory
}

$infPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.inf"
$catPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.cat"
$manifestPath = Join-Path $PackageDirectory "package-manifest.json"
$cerPath = Join-Path $PackageDirectory "NatsxAoaWinUsb.CI-Test.cer"

foreach ($path in @($infPath, $manifestPath)) {
    if (-not (Test-Path $path)) { throw "Required package file missing: $path" }
}

function Find-WdkTool {
    param([Parameter(Mandatory = $true)][string]$Name)

    $roots = @(
        (Join-Path $scriptRoot "..\aoa-bootstrap\packages"),
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
    return $matches[0].FullName
}

$inf2Cat = Find-WdkTool -Name "inf2cat.exe"
$signTool = Find-WdkTool -Name "signtool.exe"

if ([string]::IsNullOrWhiteSpace($inf2Cat) -or
    [string]::IsNullOrWhiteSpace($signTool)) {
    throw "Required WDK signing tools were not found."
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

    if (Test-Path $catPath) {
        Remove-Item -Path $catPath -Force
    }

    $osTargets = "10_VB_X64,10_CO_X64,10_NI_X64,10_GE_X64,10_25H2_X64"
    & $inf2Cat /v "/driver:$PackageDirectory" "/os:$osTargets"
    if ($LASTEXITCODE -ne 0) {
        throw "Inf2Cat failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $catPath)) {
        throw "Expected AOA WinUSB catalog was not produced."
    }

    & $signTool sign /v /fd SHA256 /sha1 $cert.Thumbprint /s My $catPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed while signing the AOA WinUSB catalog."
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member SigningMode "ephemeral-ci-test" -Force
    $manifest | Add-Member TestCertificateSubject $cert.Subject -Force
    $manifest | Add-Member TestCertificateThumbprint $cert.Thumbprint -Force
    $manifest | Add-Member TestCertificateNotAfterUtc $cert.NotAfter.ToUniversalTime().ToString("O") -Force
    $manifest | Add-Member CatalogSha256 ((Get-FileHash $catPath -Algorithm SHA256).Hash) -Force
    $manifest | Add-Member TestCertificateSha256 ((Get-FileHash $cerPath -Algorithm SHA256).Hash) -Force
    $manifest | Add-Member CatalogCreatedFor $osTargets -Force
    $manifest | ConvertTo-Json -Depth 6 | Set-Content $manifestPath -Encoding UTF8

    Write-Host "AOA WinUSB ephemeral CI test package signed."
    Write-Host "Certificate thumbprint: $($cert.Thumbprint)"
}
finally {
    Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue
}
