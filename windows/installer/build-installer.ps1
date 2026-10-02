param(
    [string]$PublishDir = "artifacts/windows-receiver",
    [string]$OutputDir = "artifacts/installer"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$version = (Get-Content (Join-Path $repoRoot "VERSION") -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION is empty."
}

$publishPath = (Resolve-Path (Join-Path $repoRoot $PublishDir)).Path
$outputPath = Join-Path $repoRoot $OutputDir
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\\ISCC.exe"
    if (Test-Path $candidate) {
        $iscc = Get-Item $candidate
    }
}

if ($null -eq $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found."
}

$script = Join-Path $PSScriptRoot "NatsxController.iss"
& $iscc.Source "/DMyAppVersion=$version" "/DPublishDir=$publishPath" "/O$outputPath" $script

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created in $outputPath"
