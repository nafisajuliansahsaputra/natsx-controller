[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppRoot
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Production USB driver installation requires administrator elevation."
    }
}

function Invoke-PnpUtil {
    param(
        [Parameter(Mandatory = $true)][string]$PnpUtil,
        [Parameter(Mandatory = $true)][string]$InfPath
    )

    if (-not (Test-Path -LiteralPath $InfPath -PathType Leaf)) {
        throw "Driver INF is missing from installed application files: $InfPath"
    }

    & $PnpUtil /add-driver $InfPath /install

    if ($LASTEXITCODE -ne 0) {
        throw "PnPUtil failed to stage/install '$InfPath' with exit code $LASTEXITCODE."
    }
}

Assert-Administrator

$root = (Resolve-Path -LiteralPath $AppRoot).Path
$pnputil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$bootstrapInf = Join-Path $root "drivers\aoa-bootstrap\Natsx.AoaBootstrap.OPPOA58.Extension.inf"
$winUsbInf = Join-Path $root "drivers\aoa-winusb\NatsxAoaWinUsb.inf"

Invoke-PnpUtil -PnpUtil $pnputil -InfPath $bootstrapInf
Invoke-PnpUtil -PnpUtil $pnputil -InfPath $winUsbInf

$stateDirectory = Join-Path $env:ProgramData "NATSX\Controller"
$statePath = Join-Path $stateDirectory "installed-usb-drivers.json"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

try {
    Import-Module Dism -ErrorAction Stop

    $expectedOriginalNames = @(
        "Natsx.AoaBootstrap.OPPOA58.Extension.inf",
        "NatsxAoaWinUsb.inf"
    )

    $records = @(
        Get-WindowsDriver -Online -All |
            Where-Object {
                $leaf = Split-Path -Leaf ([string]$_.OriginalFileName)
                $expectedOriginalNames -contains $leaf
            } |
            Select-Object Driver, OriginalFileName, ProviderName, ClassName, Version
    )

    [ordered]@{
        Version = 1
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        Drivers = $records
    } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $statePath -Encoding UTF8
}
catch {
    [ordered]@{
        Version = 1
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        Drivers = @()
        StateCaptureError = $_.Exception.Message
    } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $statePath -Encoding UTF8
}

Write-Host "NATSX production USB driver packages staged successfully."
