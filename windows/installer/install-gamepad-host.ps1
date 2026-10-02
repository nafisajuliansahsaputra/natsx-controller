[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppRoot
)

$ErrorActionPreference = "Stop"

$serviceName = "NatsxControllerGamepadHost"
$appRootPath = (Resolve-Path -LiteralPath $AppRoot).Path
$hostPath = Join-Path $appRootPath "Natsx.Controller.GamepadHost.exe"

if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Privileged gamepad-host executable is missing: $hostPath"
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & sc.exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw ("sc.exe " + ($Arguments -join " ") + " failed with exit code $exitCode." + [Environment]::NewLine + ($output -join [Environment]::NewLine))
    }

    return @($exitCode, $output)
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExpectedStatus,
        [int]$TimeoutSeconds = 20
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

        if ($null -ne $service -and [string]$service.Status -eq $ExpectedStatus) {
            return
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Service '$serviceName' did not reach state '$ExpectedStatus'."
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -ne $existing) {
    if ($existing.Status -ne "Stopped") {
        Invoke-Sc -Arguments @("stop", $serviceName) -AllowFailure | Out-Null

        try {
            Wait-ServiceState -ExpectedStatus "Stopped" -TimeoutSeconds 15
        }
        catch {
            $wmi = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
            if ($null -ne $wmi -and $wmi.ProcessId -gt 0) {
                Stop-Process -Id $wmi.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Invoke-Sc -Arguments @("delete", $serviceName) -AllowFailure | Out-Null

    $deleteDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        if ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deleteDeadline)

    if ($null -ne (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        throw "Existing service '$serviceName' could not be removed before reinstall."
    }
}

$quotedHost = '"' + $hostPath + '" --service'

try {
    Invoke-Sc -Arguments @(
        "create",
        $serviceName,
        "binPath= $quotedHost",
        "start= auto",
        "obj= LocalSystem",
        "DisplayName= NATSX Controller Gamepad Host"
    ) | Out-Null

    Invoke-Sc -Arguments @(
        "description",
        $serviceName,
        "Minimal privileged host for the NATSX Xbox 360 virtual controller. Network and transport parsing remain in the unelevated Receiver."
    ) | Out-Null

    Invoke-Sc -Arguments @(
        "failure",
        $serviceName,
        "reset= 86400",
        "actions= restart/2000/restart/5000/restart/10000"
    ) | Out-Null

    Invoke-Sc -Arguments @("failureflag", $serviceName, "1") | Out-Null
    Invoke-Sc -Arguments @("start", $serviceName) | Out-Null

    Wait-ServiceState -ExpectedStatus "Running" -TimeoutSeconds 20

    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop

    if ($service.StartMode -ne "Auto") {
        throw "Gamepad-host service is not configured for automatic startup."
    }

    if ($service.StartName -ne "LocalSystem") {
        throw "Gamepad-host service is not running under LocalSystem."
    }

    Write-Host "NATSX privileged gamepad-host service installed and running."
}
catch {
    Invoke-Sc -Arguments @("stop", $serviceName) -AllowFailure | Out-Null
    Invoke-Sc -Arguments @("delete", $serviceName) -AllowFailure | Out-Null
    throw
}
