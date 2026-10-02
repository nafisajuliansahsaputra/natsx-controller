[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    exit 1
}

$pnputil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$stateDirectory = Join-Path $env:ProgramData "NATSX\Controller"
$statePath = Join-Path $stateDirectory "installed-usb-drivers.json"
$logPath = Join-Path $stateDirectory "driver-uninstall.log"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

$publishedNames = @()

if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json

        $publishedNames += @(
            $state.Drivers |
                ForEach-Object { [string]$_.Driver } |
                Where-Object { $_ -match '(?i)^oem\d+\.inf$' }
        )
    }
    catch {
        # Fall through to live Driver Store enumeration.
    }
}

try {
    Import-Module Dism -ErrorAction Stop

    $expectedOriginalNames = @(
        "Natsx.AoaBootstrap.OPPOA58.Extension.inf",
        "NatsxAoaWinUsb.inf"
    )

    $publishedNames += @(
        Get-WindowsDriver -Online -All |
            Where-Object {
                $leaf = Split-Path -Leaf ([string]$_.OriginalFileName)
                $expectedOriginalNames -contains $leaf
            } |
            ForEach-Object { [string]$_.Driver } |
            Where-Object { $_ -match '(?i)^oem\d+\.inf$' }
    )
}
catch {
    Add-Content -LiteralPath $logPath -Value (
        "[$([DateTimeOffset]::UtcNow.ToString('O'))] DISM enumeration failed: " +
        $_.Exception.Message)
}

$failures = @()

foreach ($publishedName in @($publishedNames | Sort-Object -Unique)) {
    & $pnputil /delete-driver $publishedName /uninstall /force

    if ($LASTEXITCODE -ne 0) {
        $failures += "$publishedName (exit $LASTEXITCODE)"
    }
}

if ($failures.Count -eq 0) {
    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue

    Add-Content -LiteralPath $logPath -Value (
        "[$([DateTimeOffset]::UtcNow.ToString('O'))] Production USB driver cleanup completed.")

    exit 0
}

Add-Content -LiteralPath $logPath -Value (
    "[$([DateTimeOffset]::UtcNow.ToString('O'))] Driver cleanup incomplete: " +
    ($failures -join ", "))

# Do not block application uninstall if Windows temporarily holds a driver
# package. The retained state/log allows explicit follow-up cleanup.
exit 0
