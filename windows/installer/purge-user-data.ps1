param(
    [switch]$PurgeTrust
)

$ErrorActionPreference = "Stop"

$settingsKey = "HKCU:\\Software\\NATSX\\Controller Receiver"
$runKey = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"

Remove-ItemProperty -Path $runKey -Name "NATSX Controller Receiver" -ErrorAction SilentlyContinue
Remove-Item -Path $settingsKey -Recurse -Force -ErrorAction SilentlyContinue

if ($PurgeTrust) {
    $trustRoot = Join-Path $env:LOCALAPPDATA "NATSX\\Controller"
    Remove-Item -Path $trustRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "NATSX trust data and local identity removed."
} else {
    Write-Host "Receiver settings/startup entry removed. Trusted pairing data was preserved."
    Write-Host "Run again with -PurgeTrust only if a full trust reset is intended."
}
