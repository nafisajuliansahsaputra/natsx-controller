[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ruleName = "NATSX.Controller.LanUdp"

Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction Stop

if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) {
    throw "NATSX firewall rule remained after removal."
}

Write-Host "NATSX local-LAN firewall rule removed."
