[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppRoot
)

$ErrorActionPreference = "Stop"

$ruleName = "NATSX.Controller.LanUdp"
$displayName = "NATSX Controller - Local LAN"
$receiverPath =
    Join-Path (
        Resolve-Path -LiteralPath $AppRoot
    ).Path "Natsx.Controller.Receiver.exe"

if (-not (Test-Path -LiteralPath $receiverPath -PathType Leaf)) {
    throw "Receiver executable is missing: $receiverPath"
}

function Remove-NatsxRule {
    Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
}

try {
    Remove-NatsxRule

    New-NetFirewallRule `
        -Name $ruleName `
        -DisplayName $displayName `
        -Description "Allows NATSX Controller secure pairing, discovery, trusted control, and realtime controller traffic from the local subnet only." `
        -Group "NATSX Controller" `
        -Enabled True `
        -Direction Inbound `
        -Action Allow `
        -Program $receiverPath `
        -Protocol UDP `
        -LocalPort "43858-43860" `
        -RemoteAddress LocalSubnet `
        -Profile Any `
        -EdgeTraversalPolicy Block |
        Out-Null

    $rule =
        Get-NetFirewallRule -Name $ruleName -ErrorAction Stop

    if ($rule.Enabled -ne "True" -or
        $rule.Direction -ne "Inbound" -or
        $rule.Action -ne "Allow") {
        throw "Created NATSX firewall rule does not have the expected enabled inbound allow policy."
    }

    $portFilter =
        $rule |
            Get-NetFirewallPortFilter

    if ($portFilter.Protocol -ne "UDP" -or
        $portFilter.LocalPort -notcontains "43858-43860") {
        throw "Created NATSX firewall rule does not have the expected UDP 43858-43860 scope."
    }

    $addressFilter =
        $rule |
            Get-NetFirewallAddressFilter

    if ($addressFilter.RemoteAddress -notcontains "LocalSubnet") {
        throw "Created NATSX firewall rule is not restricted to LocalSubnet."
    }

    Write-Host "NATSX local-LAN firewall rule installed."
}
catch {
    Remove-NatsxRule
    throw
}
