[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppRoot
)

$ErrorActionPreference = "Stop"

$serviceName = "NatsxControllerGamepadHost"
$stateDirectory = Join-Path $env:ProgramData "NATSX\Controller"
$logPath = Join-Path $stateDirectory "gamepad-host-install.log"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    $logInfo = Get-Item -LiteralPath $logPath
    if ($logInfo.Length -gt 524288) {
        Remove-Item -LiteralPath $logPath -Force
    }
}

function Write-InstallLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Add-Content -LiteralPath $logPath -Value (
        "[$([DateTimeOffset]::UtcNow.ToString('O'))] " +
        $Message)
}

$appRootPath = (Resolve-Path -LiteralPath $AppRoot).Path
$programFilesPath = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFiles)

$programFilesRoot = [IO.Path]::GetFullPath(
    $programFilesPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)

$appRootFull = [IO.Path]::GetFullPath(
    $appRootPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)

$requiredPrefix =
    $programFilesRoot +
    [IO.Path]::DirectorySeparatorChar

if (-not $appRootFull.StartsWith(
        $requiredPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "Refusing to register a LocalSystem service from outside Program Files. " +
        "AppRoot='$appRootFull', required root='$programFilesRoot'.")
}

$hostPath = Join-Path $appRootFull "Natsx.Controller.GamepadHost.exe"

if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Privileged gamepad-host executable is missing: $hostPath"
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    Write-InstallLog (
        "sc.exe " +
        ($Arguments -join " "))

    $output = & sc.exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    Write-InstallLog (
        "sc.exe exit=$exitCode output=" +
        ($output -join " | "))

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

Write-InstallLog (
    "Installing/updating gamepad host from '$hostPath'.")

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$existingCim = $null
$createdNew = $null -eq $existing

if ($null -ne $existing) {
    $existingCim = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue

    if ($existing.Status -ne "Stopped") {
        Invoke-Sc -Arguments @("stop", $serviceName) -AllowFailure | Out-Null

        try {
            Wait-ServiceState -ExpectedStatus "Stopped" -TimeoutSeconds 15
        }
        catch {
            if ($null -ne $existingCim -and $existingCim.ProcessId -gt 0) {
                Stop-Process -Id $existingCim.ProcessId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

$quotedHost = '"' + $hostPath + '" --service'

try {
    if ($createdNew) {
        Invoke-Sc -Arguments @(
            "create",
            $serviceName,
            "binPath= $quotedHost",
            "start= auto",
            "obj= LocalSystem",
            "DisplayName= NATSX Controller Gamepad Host"
        ) | Out-Null
    }
    else {
        Invoke-Sc -Arguments @(
            "config",
            $serviceName,
            "binPath=",
            $quotedHost,
            "start=",
            "auto",
            "obj=",
            "LocalSystem",
            "DisplayName=",
            "NATSX Controller Gamepad Host"
        ) | Out-Null
    }

    Invoke-Sc -Arguments @(
        "description",
        $serviceName,
        "Minimal privileged host for the NATSX Xbox 360 virtual controller. Network and transport parsing remain in the unelevated Receiver."
    ) | Out-Null

    Invoke-Sc -Arguments @(
        "failure",
        $serviceName,
        "reset=",
        "86400",
        "actions=",
        "restart/2000/restart/5000/restart/10000"
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

    if ([string]$service.PathName -notlike "*Natsx.Controller.GamepadHost.exe*--service*") {
        throw "Gamepad-host service command line is incorrect."
    }

    Write-InstallLog (
        "Gamepad-host service verified Running; StartMode=$($service.StartMode); StartName=$($service.StartName); PathName=$($service.PathName)")

    Write-Host "NATSX privileged gamepad-host service installed and running."
}
catch {
    Write-InstallLog (
        "Gamepad-host install/update failed: " +
        $_.Exception.ToString())

    Invoke-Sc -Arguments @("stop", $serviceName) -AllowFailure | Out-Null

    if ($createdNew) {
        Invoke-Sc -Arguments @("delete", $serviceName) -AllowFailure | Out-Null
    }
    elseif ($null -ne $existingCim -and -not [string]::IsNullOrWhiteSpace([string]$existingCim.PathName)) {
        Invoke-Sc -Arguments @(
            "config",
            $serviceName,
            "binPath=",
            [string]$existingCim.PathName,
            "start=",
            "auto",
            "obj=",
            "LocalSystem"
        ) -AllowFailure | Out-Null

        Invoke-Sc -Arguments @("start", $serviceName) -AllowFailure | Out-Null
    }

    throw
}
